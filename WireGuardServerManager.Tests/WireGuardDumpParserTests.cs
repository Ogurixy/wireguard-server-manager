using WireGuardServerManager.Core;

namespace WireGuardServerManager.Tests;

public class WireGuardDumpParserTests
{
    [Fact]
    public void ParsesIpv4AndIpv6Endpoints()
    {
        var dump = string.Join('\n',
            "wg0\tserver-private\tserver-public\t51892\t0",
            "wg0\tpeer4\t(none)\t8.8.8.8:51820\t10.66.66.2/32\t1700000000\t100\t200\t25",
            "wg0\tpeer6\t(none)\t[2001:db8::1]:52371\t10.66.66.3/32\t0\t0\t0\t0");
        var result = WireGuardDumpParser.Parse(dump, DateTimeOffset.FromUnixTimeSeconds(1700000100), TimeSpan.FromSeconds(180));
        Assert.Equal(new[] { "wg0" }, result.Interfaces);
        Assert.Equal(51892, result.ListenPorts["wg0"]);
        Assert.Equal("8.8.8.8", result.Peers[0].PublicIp);
        Assert.Equal(51820, result.Peers[0].EndpointPort);
        Assert.Equal("2001:db8::1", result.Peers[1].PublicIp);
        Assert.Equal(52371, result.Peers[1].EndpointPort);
        Assert.True(result.Peers[0].IsOnline);
        Assert.False(result.Peers[1].IsOnline);
    }

    [Fact]
    public void ReconciliationMarksUnknownAndDatabaseOnly()
    {
        var parsed = WireGuardDumpParser.Parse("wg0\tprivate\tpublic\t51891\t0\nwg0\tknown\t(none)\t(none)\t10.0.0.2/32\t1700000000\t1\t2\t25\nwg0\tunknown\t(none)\t(none)\t10.0.0.9/32\t1700000000\t1\t2\t25", DateTimeOffset.FromUnixTimeSeconds(1700000000), TimeSpan.FromSeconds(180));
        var server = Guid.NewGuid();
        var devices = new[] { new Device(Guid.NewGuid(), server, "Known", "known", "10.0.0.2", "User"), new Device(Guid.NewGuid(), server, "Missing", "missing", "10.0.0.3", "User") };
        var result = new ReconciliationService().Reconcile(server, parsed, devices);
        Assert.Single(result.UnknownPeers);
        Assert.Single(result.DatabaseOnly);
    }

    [Fact]
    public void FindsNextVpnIpAndRejectsConflicts()
    {
        var server = new Server(Guid.NewGuid(), "US-01", "192.0.2.1", 22, "root", "wg0", "10.66.66.0/24", 51892);
        var key = Convert.ToBase64String(new byte[32]);
        var existing = new[] { new Device(Guid.NewGuid(), server.Id, "Phone", key, "10.66.66.2", "User") };
        Assert.Equal("10.66.66.3", DeviceValidation.FindNextIp(server, existing));
        Assert.Contains("VPN IP", DeviceValidation.Validate("Laptop", key, "10.66.66.2", server, existing));
    }

    [Fact]
    public void NormalizesSubnetBeforeAllocatingVpnIp()
    {
        var server = new Server(Guid.NewGuid(), "US-01", "192.0.2.1", 22, "root", "wg0", "10.66.66.99/24", 51894);
        Assert.Equal("10.66.66.2", DeviceValidation.FindNextIp(server, Array.Empty<Device>()));
        Assert.Equal("10.66.66.0/24", DeviceValidation.NormalizeIpv4Subnet("10.66.66.99/24"));
    }

    [Fact]
    public void AllocationAvoidsIpsAlreadyPresentOnServer()
    {
        var server = new Server(Guid.NewGuid(), "US-01", "192.0.2.1", 22, "root", "wg0", "10.66.66.0/24", 51894);
        Assert.Equal("10.66.66.4", DeviceValidation.FindNextIp(server, Array.Empty<Device>(), new[] { "10.66.66.2/32, 10.66.66.3/32" }));
        Assert.True(DeviceValidation.IsVpnIpReservedByAllowedIps("10.66.66.10", new[] { "10.66.66.0/24" }));
    }

    [Fact]
    public void ReconciliationReportsAllowedIpMismatchAndPeerIpConflict()
    {
        var server = Guid.NewGuid();
        var dump = "wg0\tprivate\tpublic\t51892\t0\n" +
                   "wg0\tknown\t(none)\t(none)\t10.0.0.2/32\t0\t1\t2\t25\n" +
                   "wg0\tunknown\t(none)\t(none)\t10.0.0.2/32\t0\t1\t2\t25";
        var device = new Device(Guid.NewGuid(), server, "Known", "known", "10.0.0.9", "User");
        var result = new ReconciliationService().Reconcile(server, WireGuardDumpParser.Parse(dump, DateTimeOffset.UtcNow, TimeSpan.FromSeconds(180)), new[] { device });
        Assert.Contains(result.Conflicts, x => x.StartsWith("VPN IP conflict", StringComparison.Ordinal));
        Assert.Contains(result.Conflicts, x => x.StartsWith("VPN IP mismatch", StringComparison.Ordinal));
    }
}
