using WireGuardServerManager.Core;
using Microsoft.Data.Sqlite;

namespace WireGuardServerManager.Tests;

public class SqliteStoreTests
{
    [Fact]
    public void TrafficTotalsHandleCounterResetAndCloseSession()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wg-manager-{Guid.NewGuid():N}.db");
        try
        {
            var store = new SqliteStore(path);
            store.Initialize();
            var serverId = Guid.NewGuid();
            var device = new Device(Guid.NewGuid(), serverId, "Phone", Convert.ToBase64String(new byte[32]), "10.66.66.2", "User");
            var first = Snapshot(serverId, device, 100, 200, true);
            var second = Snapshot(serverId, device, 150, 250, true);
            var reset = Snapshot(serverId, device, 10, 20, false);

            store.RecordPeerSnapshot(serverId, first, device, DateTimeOffset.UtcNow.AddMinutes(-2));
            store.RecordPeerSnapshot(serverId, second, device, DateTimeOffset.UtcNow.AddMinutes(-1));
            store.RecordPeerSnapshot(serverId, reset, device, DateTimeOffset.UtcNow);

            var total = Assert.Single(store.LoadTrafficTotals());
            Assert.Equal(60, total.RxTotal);
            Assert.Equal(70, total.TxTotal);
            var session = Assert.Single(store.LoadConnectionSessions());
            Assert.NotNull(session.DisconnectedAt);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void SecurityEventsAreDeduplicatedForRecentPeerAlerts()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wg-manager-{Guid.NewGuid():N}.db");
        try
        {
            var store = new SqliteStore(path); store.Initialize();
            var serverId = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
            var item = new SecurityEvent("UnknownPeer", "unknown", now, serverId, "peer");
            store.AddSecurityEvent(item); store.AddSecurityEvent(item);
            Assert.Single(store.LoadSecurityEvents());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ServersPersistAcrossStoreInstances()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wg-manager-{Guid.NewGuid():N}.db");
        try
        {
            var server = new Server(Guid.NewGuid(), "US-extra", "192.0.2.10", 22, "root", "wg9", "10.88.0.0/24", 51892);
            var first = new SqliteStore(path); first.Initialize(); first.AddServer(server);
            var second = new SqliteStore(path); second.Initialize();
            var loaded = Assert.Single(second.LoadServers());
            Assert.Equal(server.Host, loaded.Host); Assert.Equal("wg9", loaded.WireGuardInterface); Assert.Equal(51892, loaded.WireGuardPort);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void IgnoredPeerStatePersistsAndCanBeRestored()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wg-manager-{Guid.NewGuid():N}.db");
        try
        {
            var store = new SqliteStore(path); store.Initialize();
            var serverId = Guid.NewGuid(); var key = "ignored-peer";
            Assert.False(store.IsPeerIgnored(serverId, key));
            store.SetPeerIgnored(serverId, key, true);
            Assert.True(store.IsPeerIgnored(serverId, key));
            Assert.Single(store.LoadIgnoredPeers());
            store.SetPeerIgnored(serverId, key, false);
            Assert.False(store.IsPeerIgnored(serverId, key));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void TrafficSinceUsesPositiveDeltasAndIgnoresCounterResets()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wg-manager-{Guid.NewGuid():N}.db");
        try
        {
            var store = new SqliteStore(path); store.Initialize(); var serverId = Guid.NewGuid();
            var device = new Device(Guid.NewGuid(), serverId, "Phone", Convert.ToBase64String(new byte[32]), "10.66.66.2", "User");
            var now = DateTimeOffset.UtcNow;
            store.RecordPeerSnapshot(serverId, Snapshot(serverId, device, 100, 100, true), device, now.AddDays(-2));
            store.RecordPeerSnapshot(serverId, Snapshot(serverId, device, 150, 170, true), device, now.AddHours(-1));
            store.RecordPeerSnapshot(serverId, Snapshot(serverId, device, 5, 7, true), device, now);
            var total = store.LoadTrafficSince(now.AddDays(-1));
            Assert.Equal(55, total.Rx); Assert.Equal(77, total.Tx);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void DeviceMigrationUpdatesDeviceAndRecordsHistoryAtomically()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wg-manager-{Guid.NewGuid():N}.db");
        try
        {
            var store = new SqliteStore(path); store.Initialize();
            var from = new Server(Guid.NewGuid(), "US-01", "192.0.2.1", 22, "root", "wg0", "10.66.66.0/24", 51892);
            var to = new Server(Guid.NewGuid(), "US-02", "192.0.2.2", 22, "root", "wg0", "10.77.77.0/24", 52371);
            store.AddServer(from); store.AddServer(to);
            var device = new Device(Guid.NewGuid(), from.Id, "Phone", Convert.ToBase64String(new byte[32]), "10.66.66.2", "User");
            store.AddDevice(device);
            store.MoveDevice(device.Id, from.Id, to.Id, device.VpnIp, "10.77.77.2");
            var moved = Assert.Single(store.LoadDevices());
            Assert.Equal(to.Id, moved.ServerId); Assert.Equal("10.77.77.2", moved.VpnIp);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void SeedServersDoesNotOverwriteAdministratorEdits()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wg-manager-{Guid.NewGuid():N}.db");
        try
        {
            var id = Guid.NewGuid();
            var store = new SqliteStore(path); store.Initialize();
            store.AddServer(new Server(id, "Edited", "192.0.2.30", 2222, "admin", "wg9", "10.99.0.0/24", 60000));
            store.SeedServers(new[] { new Server(id, "Default", "192.0.2.31", 22, "root", "wg0", "10.66.66.0/24", 51892) });
            var loaded = Assert.Single(store.LoadServers());
            Assert.Equal("Edited", loaded.Name); Assert.Equal("wg9", loaded.WireGuardInterface); Assert.Equal(60000, loaded.WireGuardPort);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ForeignKeysAreEnabledForEveryConnection()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wg-manager-{Guid.NewGuid():N}.db");
        try
        {
            var store = new SqliteStore(path); store.Initialize();
            var orphan = new Device(Guid.NewGuid(), Guid.NewGuid(), "Orphan", Convert.ToBase64String(new byte[32]), "10.66.66.2", "User");
            Assert.Throws<SqliteException>(() => store.AddDevice(orphan));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void RuntimeReconciliationUpdatesOnlyInterfaceAndPort()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wg-manager-{Guid.NewGuid():N}.db");
        try
        {
            var store = new SqliteStore(path); store.Initialize();
            var original = new Server(Guid.NewGuid(), "US-02", "192.0.2.2", 22, "root", "old0", "10.77.77.0/24", 52371);
            store.AddServer(original);
            store.UpdateServerRuntime(original with { WireGuardInterface = "wg0", WireGuardPort = 52372, VpnSubnet = "10.99.99.0/24" }, original.WireGuardPort, original.WireGuardInterface, original.VpnSubnet);

            var updated = Assert.Single(store.LoadServers());
            Assert.Equal("wg0", updated.WireGuardInterface);
            Assert.Equal(52372, updated.WireGuardPort);
            Assert.Equal("10.99.99.0/24", updated.VpnSubnet);
            Assert.Equal(original.Host, updated.Host);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static WireGuardPeerSnapshot Snapshot(Guid serverId, Device device, long rx, long tx, bool online) => new(
        "wg0", device.PublicKey, "198.51.100.10:51820", "198.51.100.10", 51820, device.VpnIp + "/32",
        DateTimeOffset.UtcNow.AddSeconds(online ? -10 : -1000), rx, tx, 25, online, device, false);
}
