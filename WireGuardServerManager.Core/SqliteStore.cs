using Microsoft.Data.Sqlite;

namespace WireGuardServerManager.Core;

public sealed class SqliteStore
{
    private readonly string _connectionString;
    public SqliteStore(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath) ?? ".");
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, ForeignKeys = true }.ToString();
    }

    public void Initialize()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys = ON;
            CREATE TABLE IF NOT EXISTS Servers (
                Id TEXT PRIMARY KEY, Name TEXT NOT NULL, Host TEXT NOT NULL, SshPort INTEGER NOT NULL,
                SshUser TEXT NOT NULL, WireGuardInterface TEXT NOT NULL, VpnSubnet TEXT, WireGuardPort INTEGER NOT NULL,
                Enabled INTEGER NOT NULL DEFAULT 1, CreatedAt TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS Devices (
                Id TEXT PRIMARY KEY, ServerId TEXT NOT NULL, Name TEXT NOT NULL, PublicKey TEXT NOT NULL UNIQUE,
                VpnIp TEXT NOT NULL, UserName TEXT NOT NULL, Enabled INTEGER NOT NULL DEFAULT 1,
                DeletedAt TEXT, FOREIGN KEY(ServerId) REFERENCES Servers(Id)
            );
            CREATE TABLE IF NOT EXISTS SecurityEvents (
                Id INTEGER PRIMARY KEY AUTOINCREMENT, Type TEXT NOT NULL, Description TEXT NOT NULL,
                OccurredAt TEXT NOT NULL, ServerId TEXT, PublicKey TEXT
            );
            CREATE TABLE IF NOT EXISTS AuditLogs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT, Action TEXT NOT NULL, Details TEXT NOT NULL, OccurredAt TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS TrafficSnapshots (
                Id INTEGER PRIMARY KEY AUTOINCREMENT, ServerId TEXT NOT NULL, DeviceId TEXT, PublicKey TEXT NOT NULL,
                VpnIp TEXT, CapturedAt TEXT NOT NULL, Rx INTEGER NOT NULL, Tx INTEGER NOT NULL,
                CounterReset INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS TrafficTotals (
                ServerId TEXT NOT NULL, DeviceId TEXT, PublicKey TEXT NOT NULL, RxTotal INTEGER NOT NULL DEFAULT 0,
                TxTotal INTEGER NOT NULL DEFAULT 0, UpdatedAt TEXT NOT NULL, PRIMARY KEY(ServerId, PublicKey)
            );
            CREATE TABLE IF NOT EXISTS ConnectionSessions (
                Id TEXT PRIMARY KEY, DeviceId TEXT NOT NULL, ServerId TEXT NOT NULL, PublicIp TEXT, VpnIp TEXT NOT NULL,
                ConnectedAt TEXT NOT NULL, DisconnectedAt TEXT, LastHandshake TEXT, Rx INTEGER NOT NULL, Tx INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS Users (
                Id TEXT PRIMARY KEY, Name TEXT NOT NULL UNIQUE, Enabled INTEGER NOT NULL DEFAULT 1, CreatedAt TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS AccessRules (
                Id TEXT PRIMARY KEY, Type TEXT NOT NULL, Value TEXT NOT NULL, Description TEXT NOT NULL DEFAULT '',
                Enabled INTEGER NOT NULL DEFAULT 1, CreatedAt TEXT NOT NULL, UNIQUE(Type, Value)
            );
            CREATE TABLE IF NOT EXISTS AppSettings (
                Key TEXT PRIMARY KEY, Value TEXT NOT NULL, UpdatedAt TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS MigrationHistory (
                Id INTEGER PRIMARY KEY AUTOINCREMENT, DeviceId TEXT NOT NULL, FromServerId TEXT NOT NULL,
                ToServerId TEXT NOT NULL, FromVpnIp TEXT NOT NULL, ToVpnIp TEXT NOT NULL, OccurredAt TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS IgnoredPeers (
                ServerId TEXT NOT NULL, PublicKey TEXT NOT NULL, IgnoredAt TEXT NOT NULL,
                PRIMARY KEY(ServerId, PublicKey)
            );
            CREATE INDEX IF NOT EXISTS IX_Devices_PublicKey ON Devices(PublicKey);
            CREATE INDEX IF NOT EXISTS IX_Devices_ServerId ON Devices(ServerId);
            CREATE INDEX IF NOT EXISTS IX_TrafficSnapshots_ServerPeer ON TrafficSnapshots(ServerId, PublicKey, CapturedAt);
            CREATE INDEX IF NOT EXISTS IX_ConnectionSessions_Device ON ConnectionSessions(DeviceId, ConnectedAt);
            """;
        command.ExecuteNonQuery();
    }

    public void SeedServers(IEnumerable<Server> servers)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        foreach (var server in servers)
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO Servers (Id, Name, Host, SshPort, SshUser, WireGuardInterface, VpnSubnet, WireGuardPort, Enabled, CreatedAt)
                VALUES ($id,$name,$host,$sshPort,$sshUser,$iface,$subnet,$wgPort,1,$created)
                ON CONFLICT(Id) DO NOTHING;
                """;
            command.Parameters.AddWithValue("$id", server.Id.ToString()); command.Parameters.AddWithValue("$name", server.Name);
            command.Parameters.AddWithValue("$host", server.Host); command.Parameters.AddWithValue("$sshPort", server.SshPort);
            command.Parameters.AddWithValue("$sshUser", server.SshUser); command.Parameters.AddWithValue("$iface", server.WireGuardInterface);
            command.Parameters.AddWithValue("$subnet", (object?)server.VpnSubnet ?? DBNull.Value); command.Parameters.AddWithValue("$wgPort", server.WireGuardPort);
            command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O")); command.ExecuteNonQuery();
        }
    }

    public void AddServer(Server server)
    {
        using var connection = new SqliteConnection(_connectionString); connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO Servers (Id,Name,Host,SshPort,SshUser,WireGuardInterface,VpnSubnet,WireGuardPort,Enabled,CreatedAt) VALUES ($id,$name,$host,$sshPort,$sshUser,$iface,$subnet,$wgPort,1,$created)";
        command.Parameters.AddWithValue("$id", server.Id.ToString()); command.Parameters.AddWithValue("$name", server.Name); command.Parameters.AddWithValue("$host", server.Host); command.Parameters.AddWithValue("$sshPort", server.SshPort);
        command.Parameters.AddWithValue("$sshUser", server.SshUser); command.Parameters.AddWithValue("$iface", server.WireGuardInterface); command.Parameters.AddWithValue("$subnet", (object?)server.VpnSubnet ?? DBNull.Value); command.Parameters.AddWithValue("$wgPort", server.WireGuardPort); command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O")); command.ExecuteNonQuery();
        AddAuditLog("ServerCreated", $"Server={server.Id}; Name={server.Name}; Host={server.Host}");
    }

    public void UpdateServer(Server server)
    {
        using var connection = new SqliteConnection(_connectionString); connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Servers SET Name=$name,Host=$host,SshPort=$sshPort,SshUser=$sshUser,WireGuardInterface=$iface,VpnSubnet=$subnet,WireGuardPort=$wgPort,Enabled=$enabled WHERE Id=$id";
        command.Parameters.AddWithValue("$id", server.Id.ToString()); command.Parameters.AddWithValue("$name", server.Name); command.Parameters.AddWithValue("$host", server.Host);
        command.Parameters.AddWithValue("$sshPort", server.SshPort); command.Parameters.AddWithValue("$sshUser", server.SshUser); command.Parameters.AddWithValue("$iface", server.WireGuardInterface);
        command.Parameters.AddWithValue("$subnet", (object?)server.VpnSubnet ?? DBNull.Value); command.Parameters.AddWithValue("$wgPort", server.WireGuardPort); command.Parameters.AddWithValue("$enabled", server.Enabled ? 1 : 0);
        if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("服务器记录不存在，无法更新。");
        AddAuditLog("ServerUpdated", $"Server={server.Id}; Name={server.Name}; Host={server.Host}; WireGuardPort={server.WireGuardPort}");
    }

    public void UpdateServerRuntime(Server server, int previousPort, string previousInterface, string? previousSubnet)
    {
        using var connection = new SqliteConnection(_connectionString); connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Servers SET WireGuardInterface=$iface,WireGuardPort=$wgPort,VpnSubnet=$subnet WHERE Id=$id";
        command.Parameters.AddWithValue("$id", server.Id.ToString());
        command.Parameters.AddWithValue("$iface", server.WireGuardInterface);
        command.Parameters.AddWithValue("$wgPort", server.WireGuardPort);
        command.Parameters.AddWithValue("$subnet", (object?)server.VpnSubnet ?? DBNull.Value);
        if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("服务器记录不存在，无法同步运行状态。");
        AddAuditLog("ServerRuntimeReconciled", $"Server={server.Id}; Interface={previousInterface}->{server.WireGuardInterface}; WireGuardPort={previousPort}->{server.WireGuardPort}; VpnSubnet={previousSubnet}->{server.VpnSubnet}");
    }

    public IReadOnlyList<Server> LoadServers()
    {
        using var connection = new SqliteConnection(_connectionString); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = "SELECT Id,Name,Host,SshPort,SshUser,WireGuardInterface,VpnSubnet,WireGuardPort,Enabled FROM Servers ORDER BY Name";
        using var reader = command.ExecuteReader(); var result = new List<Server>();
        while (reader.Read()) result.Add(new Server(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetInt32(3), reader.GetString(4), reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetInt32(7), reader.GetInt64(8) == 1));
        return result;
    }

    public IReadOnlyList<Device> LoadDevices()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ServerId, Name, PublicKey, VpnIp, UserName, Enabled FROM Devices WHERE DeletedAt IS NULL ORDER BY Name";
        using var reader = command.ExecuteReader();
        var devices = new List<Device>();
        while (reader.Read())
            devices.Add(new Device(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetInt64(6) == 1));
        return devices;
    }

    public void AddDevice(Device device)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO Devices (Id, ServerId, Name, PublicKey, VpnIp, UserName, Enabled) VALUES ($id,$server,$name,$key,$ip,$user,1)";
        command.Parameters.AddWithValue("$id", device.Id.ToString());
        command.Parameters.AddWithValue("$server", device.ServerId.ToString());
        command.Parameters.AddWithValue("$name", device.Name);
        command.Parameters.AddWithValue("$key", device.PublicKey);
        command.Parameters.AddWithValue("$ip", device.VpnIp);
        command.Parameters.AddWithValue("$user", device.UserName);
        command.ExecuteNonQuery();
        AddAuditLog("DeviceCreated", $"Device={device.Id}; Server={device.ServerId}; PublicKey={device.PublicKey}");
    }

    public void SetDeviceEnabled(Guid deviceId, bool enabled)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Devices SET Enabled=$enabled WHERE Id=$id AND DeletedAt IS NULL";
        command.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);
        command.Parameters.AddWithValue("$id", deviceId.ToString());
        command.ExecuteNonQuery();
        AddAuditLog(enabled ? "DeviceEnabled" : "DeviceDisabled", $"Device={deviceId}");
    }

    public void SoftDeleteDevice(Guid deviceId)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Devices SET Enabled=0, DeletedAt=$deleted WHERE Id=$id AND DeletedAt IS NULL";
        command.Parameters.AddWithValue("$deleted", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id", deviceId.ToString());
        command.ExecuteNonQuery();
        AddAuditLog("DeviceSoftDeleted", $"Device={deviceId}");
    }

    public bool DevicePublicKeyExists(string publicKey, Guid? excluding = null)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM Devices WHERE PublicKey=$key AND DeletedAt IS NULL" + (excluding is null ? string.Empty : " AND Id<>$id");
        command.Parameters.AddWithValue("$key", publicKey);
        if (excluding is not null) command.Parameters.AddWithValue("$id", excluding.Value.ToString());
        return Convert.ToInt64(command.ExecuteScalar()) > 0;
    }

    public bool DeviceVpnIpExists(string vpnIp, Guid serverId, Guid? excluding = null)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM Devices WHERE VpnIp=$ip AND ServerId=$server AND DeletedAt IS NULL" + (excluding is null ? string.Empty : " AND Id<>$id");
        command.Parameters.AddWithValue("$ip", vpnIp); command.Parameters.AddWithValue("$server", serverId.ToString());
        if (excluding is not null) command.Parameters.AddWithValue("$id", excluding.Value.ToString());
        return Convert.ToInt64(command.ExecuteScalar()) > 0;
    }

    public void RecordPeerSnapshot(Guid serverId, WireGuardPeerSnapshot peer, Device? device, DateTimeOffset now, TimeSpan? offlineGracePeriod = null, TimeSpan? onlineThreshold = null)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        long? previousRx = null, previousTx = null;
        using (var previous = connection.CreateCommand())
        {
            previous.Transaction = transaction;
            previous.CommandText = "SELECT Rx, Tx FROM TrafficSnapshots WHERE ServerId=$server AND PublicKey=$key ORDER BY Id DESC LIMIT 1";
            previous.Parameters.AddWithValue("$server", serverId.ToString()); previous.Parameters.AddWithValue("$key", peer.PublicKey);
            using var reader = previous.ExecuteReader();
            if (reader.Read()) { previousRx = reader.GetInt64(0); previousTx = reader.GetInt64(1); }
        }
        var rxReset = previousRx is not null && peer.TransferRx < previousRx;
        var txReset = previousTx is not null && peer.TransferTx < previousTx;
        var reset = rxReset || txReset;
        var rxDelta = previousRx is null ? 0 : rxReset ? peer.TransferRx : peer.TransferRx - previousRx.Value;
        var txDelta = previousTx is null ? 0 : txReset ? peer.TransferTx : peer.TransferTx - previousTx.Value;
        using (var snapshot = connection.CreateCommand())
        {
            snapshot.Transaction = transaction;
            snapshot.CommandText = "INSERT INTO TrafficSnapshots (ServerId,DeviceId,PublicKey,VpnIp,CapturedAt,Rx,Tx,CounterReset) VALUES ($server,$device,$key,$ip,$at,$rx,$tx,$reset)";
            snapshot.Parameters.AddWithValue("$server", serverId.ToString()); snapshot.Parameters.AddWithValue("$device", (object?)device?.Id.ToString() ?? DBNull.Value);
            snapshot.Parameters.AddWithValue("$key", peer.PublicKey); snapshot.Parameters.AddWithValue("$ip", (object?)peer.AllowedIps ?? DBNull.Value);
            snapshot.Parameters.AddWithValue("$at", now.ToString("O")); snapshot.Parameters.AddWithValue("$rx", peer.TransferRx);
            snapshot.Parameters.AddWithValue("$tx", peer.TransferTx); snapshot.Parameters.AddWithValue("$reset", reset ? 1 : 0); snapshot.ExecuteNonQuery();
        }
        using (var total = connection.CreateCommand())
        {
            total.Transaction = transaction;
            total.CommandText = "INSERT INTO TrafficTotals (ServerId,DeviceId,PublicKey,RxTotal,TxTotal,UpdatedAt) VALUES ($server,$device,$key,$rx,$tx,$at) ON CONFLICT(ServerId,PublicKey) DO UPDATE SET DeviceId=$device,RxTotal=RxTotal+$rx,TxTotal=TxTotal+$tx,UpdatedAt=$at";
            total.Parameters.AddWithValue("$server", serverId.ToString()); total.Parameters.AddWithValue("$device", (object?)device?.Id.ToString() ?? DBNull.Value);
            total.Parameters.AddWithValue("$key", peer.PublicKey); total.Parameters.AddWithValue("$rx", rxDelta); total.Parameters.AddWithValue("$tx", txDelta); total.Parameters.AddWithValue("$at", now.ToString("O")); total.ExecuteNonQuery();
        }
        if (device is not null)
        {
            using var open = connection.CreateCommand();
            open.Transaction = transaction;
            open.CommandText = "SELECT Id FROM ConnectionSessions WHERE DeviceId=$device AND ServerId=$server AND DisconnectedAt IS NULL LIMIT 1";
            open.Parameters.AddWithValue("$device", device.Id.ToString()); open.Parameters.AddWithValue("$server", serverId.ToString());
            var openId = open.ExecuteScalar()?.ToString();
            var keepSessionOpen = peer.IsOnline || (openId is not null && offlineGracePeriod is { } grace && peer.LatestHandshake is { } handshake && now >= handshake && now - handshake <= grace + (onlineThreshold ?? TimeSpan.FromSeconds(180)));
            if (peer.IsOnline && openId is null)
            {
                using var start = connection.CreateCommand();
                start.Transaction = transaction;
                start.CommandText = "INSERT INTO ConnectionSessions (Id,DeviceId,ServerId,PublicIp,VpnIp,ConnectedAt,LastHandshake,Rx,Tx) VALUES ($id,$device,$server,$publicIp,$ip,$connected,$handshake,$rx,$tx)";
                start.Parameters.AddWithValue("$id", Guid.NewGuid().ToString()); start.Parameters.AddWithValue("$device", device.Id.ToString()); start.Parameters.AddWithValue("$server", serverId.ToString());
                start.Parameters.AddWithValue("$publicIp", (object?)peer.PublicIp ?? DBNull.Value); start.Parameters.AddWithValue("$ip", peer.AllowedIps); start.Parameters.AddWithValue("$connected", now.ToString("O"));
                start.Parameters.AddWithValue("$handshake", (object?)peer.LatestHandshake?.ToString("O") ?? DBNull.Value); start.Parameters.AddWithValue("$rx", peer.TransferRx); start.Parameters.AddWithValue("$tx", peer.TransferTx); start.ExecuteNonQuery();
            }
            else if (openId is not null)
            {
                using var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = keepSessionOpen
                    ? "UPDATE ConnectionSessions SET PublicIp=$publicIp,LastHandshake=$handshake,Rx=$rx,Tx=$tx WHERE Id=$id"
                    : "UPDATE ConnectionSessions SET DisconnectedAt=$disconnected,LastHandshake=$handshake,Rx=$rx,Tx=$tx WHERE Id=$id";
                update.Parameters.AddWithValue("$id", openId); update.Parameters.AddWithValue("$publicIp", (object?)peer.PublicIp ?? DBNull.Value);
                update.Parameters.AddWithValue("$handshake", (object?)peer.LatestHandshake?.ToString("O") ?? DBNull.Value); update.Parameters.AddWithValue("$rx", peer.TransferRx); update.Parameters.AddWithValue("$tx", peer.TransferTx);
                if (!peer.IsOnline) update.Parameters.AddWithValue("$disconnected", now.ToString("O"));
                update.ExecuteNonQuery();
            }
        }
        transaction.Commit();
    }

    public IReadOnlyList<ConnectionSession> LoadConnectionSessions(int limit = 500)
    {
        using var connection = new SqliteConnection(_connectionString); connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id,DeviceId,ServerId,PublicIp,VpnIp,ConnectedAt,DisconnectedAt,LastHandshake,Rx,Tx FROM ConnectionSessions ORDER BY ConnectedAt DESC LIMIT $limit";
        command.Parameters.AddWithValue("$limit", limit);
        using var reader = command.ExecuteReader(); var result = new List<ConnectionSession>();
        while (reader.Read()) result.Add(new ConnectionSession(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), Guid.Parse(reader.GetString(2)), reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetString(4), DateTimeOffset.Parse(reader.GetString(5)), reader.IsDBNull(6) ? null : DateTimeOffset.Parse(reader.GetString(6)), reader.IsDBNull(7) ? null : DateTimeOffset.Parse(reader.GetString(7)), reader.GetInt64(8), reader.GetInt64(9)));
        return result;
    }

    public void CloseOpenSession(Guid deviceId, Guid serverId, DateTimeOffset disconnectedAt)
    {
        using var connection = new SqliteConnection(_connectionString); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = "UPDATE ConnectionSessions SET DisconnectedAt=$at WHERE DeviceId=$device AND ServerId=$server AND DisconnectedAt IS NULL";
        command.Parameters.AddWithValue("$at", disconnectedAt.ToString("O")); command.Parameters.AddWithValue("$device", deviceId.ToString()); command.Parameters.AddWithValue("$server", serverId.ToString());
        if (command.ExecuteNonQuery() > 0) AddAuditLog("ConnectionClosed", $"Device={deviceId}; Server={serverId}; Reason=PeerMissing");
    }

    public void MoveDevice(Guid deviceId, Guid fromServerId, Guid toServerId, string fromVpnIp, string toVpnIp)
    {
        using var connection = new SqliteConnection(_connectionString); connection.Open();
        using var transaction = connection.BeginTransaction();
        using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction; update.CommandText = "UPDATE Devices SET ServerId=$toServer,VpnIp=$toIp WHERE Id=$device AND ServerId=$fromServer AND DeletedAt IS NULL";
            update.Parameters.AddWithValue("$toServer", toServerId.ToString()); update.Parameters.AddWithValue("$toIp", toVpnIp); update.Parameters.AddWithValue("$device", deviceId.ToString()); update.Parameters.AddWithValue("$fromServer", fromServerId.ToString());
            if (update.ExecuteNonQuery() != 1) throw new InvalidOperationException("设备迁移时数据库记录已变化，操作已停止。");
        }
        using (var history = connection.CreateCommand())
        {
            history.Transaction = transaction; history.CommandText = "INSERT INTO MigrationHistory (DeviceId,FromServerId,ToServerId,FromVpnIp,ToVpnIp,OccurredAt) VALUES ($device,$fromServer,$toServer,$fromIp,$toIp,$at)";
            history.Parameters.AddWithValue("$device", deviceId.ToString()); history.Parameters.AddWithValue("$fromServer", fromServerId.ToString()); history.Parameters.AddWithValue("$toServer", toServerId.ToString()); history.Parameters.AddWithValue("$fromIp", fromVpnIp); history.Parameters.AddWithValue("$toIp", toVpnIp); history.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O")); history.ExecuteNonQuery();
        }
        transaction.Commit();
        AddAuditLog("DeviceMigrated", $"Device={deviceId}; From={fromServerId}/{fromVpnIp}; To={toServerId}/{toVpnIp}");
    }

    public IReadOnlyList<TrafficTotal> LoadTrafficTotals()
    {
        using var connection = new SqliteConnection(_connectionString); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = "SELECT ServerId,DeviceId,PublicKey,RxTotal,TxTotal,UpdatedAt FROM TrafficTotals ORDER BY UpdatedAt DESC";
        using var reader = command.ExecuteReader(); var result = new List<TrafficTotal>();
        while (reader.Read()) result.Add(new TrafficTotal(Guid.Parse(reader.GetString(0)), reader.IsDBNull(1) ? null : Guid.Parse(reader.GetString(1)), reader.GetString(2), reader.GetInt64(3), reader.GetInt64(4), DateTimeOffset.Parse(reader.GetString(5))));
        return result;
    }

    public (long Rx, long Tx) LoadTrafficSince(DateTimeOffset from)
    {
        using var connection = new SqliteConnection(_connectionString); connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ServerId,PublicKey,CapturedAt,Rx,Tx FROM TrafficSnapshots ORDER BY ServerId,PublicKey,CapturedAt,Id";
        using var reader = command.ExecuteReader();
        var previous = new Dictionary<(string Server, string Key), (long Rx, long Tx)>();
        long rxTotal = 0, txTotal = 0;
        while (reader.Read())
        {
            var server = reader.GetString(0); var key = reader.GetString(1); var captured = DateTimeOffset.Parse(reader.GetString(2));
            var currentRx = reader.GetInt64(3); var currentTx = reader.GetInt64(4); var id = (server, key);
            if (previous.TryGetValue(id, out var prior) && captured >= from)
            {
                rxTotal += currentRx >= prior.Rx ? currentRx - prior.Rx : currentRx;
                txTotal += currentTx >= prior.Tx ? currentTx - prior.Tx : currentTx;
            }
            previous[id] = (currentRx, currentTx);
        }
        return (rxTotal, txTotal);
    }

    public IReadOnlyList<SecurityEvent> LoadSecurityEvents(int limit = 200)
    {
        using var connection = new SqliteConnection(_connectionString); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = "SELECT Type,Description,OccurredAt,ServerId,PublicKey FROM SecurityEvents ORDER BY OccurredAt DESC LIMIT $limit"; command.Parameters.AddWithValue("$limit", limit);
        using var reader = command.ExecuteReader(); var result = new List<SecurityEvent>();
        while (reader.Read()) result.Add(new SecurityEvent(reader.GetString(0), reader.GetString(1), DateTimeOffset.Parse(reader.GetString(2)), reader.IsDBNull(3) ? null : Guid.Parse(reader.GetString(3)), reader.IsDBNull(4) ? null : reader.GetString(4)));
        return result;
    }

    public bool IsPeerIgnored(Guid serverId, string publicKey)
    {
        using var connection = new SqliteConnection(_connectionString); connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM IgnoredPeers WHERE ServerId=$server AND PublicKey=$key LIMIT 1";
        command.Parameters.AddWithValue("$server", serverId.ToString()); command.Parameters.AddWithValue("$key", publicKey);
        return command.ExecuteScalar() is not null;
    }

    public void SetPeerIgnored(Guid serverId, string publicKey, bool ignored)
    {
        using var connection = new SqliteConnection(_connectionString); connection.Open();
        using var command = connection.CreateCommand();
        if (ignored)
        {
            command.CommandText = "INSERT INTO IgnoredPeers (ServerId,PublicKey,IgnoredAt) VALUES ($server,$key,$at) ON CONFLICT(ServerId,PublicKey) DO UPDATE SET IgnoredAt=$at";
            command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        }
        else
        {
            command.CommandText = "DELETE FROM IgnoredPeers WHERE ServerId=$server AND PublicKey=$key";
        }
        command.Parameters.AddWithValue("$server", serverId.ToString()); command.Parameters.AddWithValue("$key", publicKey); command.ExecuteNonQuery();
        AddAuditLog(ignored ? "UnknownPeerIgnored" : "UnknownPeerIgnoreRemoved", $"Server={serverId}; PublicKey={publicKey}");
    }

    public IReadOnlyList<IgnoredPeer> LoadIgnoredPeers()
    {
        using var connection = new SqliteConnection(_connectionString); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = "SELECT ServerId,PublicKey,IgnoredAt FROM IgnoredPeers ORDER BY IgnoredAt DESC";
        using var reader = command.ExecuteReader(); var result = new List<IgnoredPeer>();
        while (reader.Read()) result.Add(new IgnoredPeer(Guid.Parse(reader.GetString(0)), reader.GetString(1), DateTimeOffset.Parse(reader.GetString(2))));
        return result;
    }

    public IReadOnlyList<AuditLog> LoadAuditLogs(int limit = 300)
    {
        using var connection = new SqliteConnection(_connectionString); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = "SELECT Id,Action,Details,OccurredAt FROM AuditLogs ORDER BY Id DESC LIMIT $limit"; command.Parameters.AddWithValue("$limit", limit);
        using var reader = command.ExecuteReader(); var result = new List<AuditLog>();
        while (reader.Read()) result.Add(new AuditLog(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), DateTimeOffset.Parse(reader.GetString(3))));
        return result;
    }

    public void AddSecurityEvent(SecurityEvent securityEvent)
    {
        using var connection = new SqliteConnection(_connectionString); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = "INSERT INTO SecurityEvents (Type,Description,OccurredAt,ServerId,PublicKey) SELECT $type,$description,$at,$server,$key WHERE NOT EXISTS (SELECT 1 FROM SecurityEvents WHERE Type=$type AND ServerId IS $server AND PublicKey IS $key AND OccurredAt >= $recent)";
        command.Parameters.AddWithValue("$type", securityEvent.Type); command.Parameters.AddWithValue("$description", securityEvent.Description); command.Parameters.AddWithValue("$at", securityEvent.OccurredAt.ToString("O"));
        command.Parameters.AddWithValue("$server", (object?)securityEvent.ServerId?.ToString() ?? DBNull.Value); command.Parameters.AddWithValue("$key", (object?)securityEvent.PublicKey ?? DBNull.Value); command.Parameters.AddWithValue("$recent", securityEvent.OccurredAt.AddMinutes(-5).ToString("O")); command.ExecuteNonQuery();
    }

    public IReadOnlyList<UserRecord> LoadUsers()
    {
        using var connection = new SqliteConnection(_connectionString); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = "SELECT Id,Name,Enabled,CreatedAt FROM Users ORDER BY Name";
        using var reader = command.ExecuteReader(); var result = new List<UserRecord>();
        while (reader.Read()) result.Add(new UserRecord(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetInt64(2) == 1, DateTimeOffset.Parse(reader.GetString(3))));
        return result;
    }

    public void AddUser(UserRecord user)
    {
        using var connection = new SqliteConnection(_connectionString); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = "INSERT INTO Users (Id,Name,Enabled,CreatedAt) VALUES ($id,$name,$enabled,$created)";
        command.Parameters.AddWithValue("$id", user.Id.ToString()); command.Parameters.AddWithValue("$name", user.Name); command.Parameters.AddWithValue("$enabled", user.Enabled ? 1 : 0); command.Parameters.AddWithValue("$created", user.CreatedAt.ToString("O")); command.ExecuteNonQuery();
        AddAuditLog("UserCreated", $"User={user.Id}; Name={user.Name}");
    }

    public void SetUserEnabled(Guid userId, bool enabled)
    {
        using var connection = new SqliteConnection(_connectionString); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = "UPDATE Users SET Enabled=$enabled WHERE Id=$id";
        command.Parameters.AddWithValue("$enabled", enabled ? 1 : 0); command.Parameters.AddWithValue("$id", userId.ToString()); command.ExecuteNonQuery();
        AddAuditLog(enabled ? "UserEnabled" : "UserDisabled", $"User={userId}");
    }

    public IReadOnlyList<AccessRule> LoadAccessRules(string type)
    {
        using var connection = new SqliteConnection(_connectionString); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = "SELECT Id,Type,Value,Description,Enabled,CreatedAt FROM AccessRules WHERE Type=$type ORDER BY Value"; command.Parameters.AddWithValue("$type", type);
        using var reader = command.ExecuteReader(); var result = new List<AccessRule>();
        while (reader.Read()) result.Add(new AccessRule(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt64(4) == 1, DateTimeOffset.Parse(reader.GetString(5))));
        return result;
    }

    public void AddAccessRule(AccessRule rule)
    {
        using var connection = new SqliteConnection(_connectionString); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = "INSERT INTO AccessRules (Id,Type,Value,Description,Enabled,CreatedAt) VALUES ($id,$type,$value,$description,$enabled,$created)";
        command.Parameters.AddWithValue("$id", rule.Id.ToString()); command.Parameters.AddWithValue("$type", rule.Type); command.Parameters.AddWithValue("$value", rule.Value); command.Parameters.AddWithValue("$description", rule.Description); command.Parameters.AddWithValue("$enabled", rule.Enabled ? 1 : 0); command.Parameters.AddWithValue("$created", rule.CreatedAt.ToString("O")); command.ExecuteNonQuery();
        AddAuditLog("AccessRuleCreated", $"Type={rule.Type}; Value={rule.Value}");
    }

    public void SetAccessRuleEnabled(Guid ruleId, bool enabled)
    {
        using var connection = new SqliteConnection(_connectionString); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = "UPDATE AccessRules SET Enabled=$enabled WHERE Id=$id";
        command.Parameters.AddWithValue("$enabled", enabled ? 1 : 0); command.Parameters.AddWithValue("$id", ruleId.ToString()); command.ExecuteNonQuery();
        AddAuditLog(enabled ? "AccessRuleEnabled" : "AccessRuleDisabled", $"Rule={ruleId}");
    }

    public void DeleteAccessRule(Guid ruleId)
    {
        using var connection = new SqliteConnection(_connectionString); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = "DELETE FROM AccessRules WHERE Id=$id";
        command.Parameters.AddWithValue("$id", ruleId.ToString()); command.ExecuteNonQuery();
        AddAuditLog("AccessRuleDeleted", $"Rule={ruleId}");
    }

    public string? GetSetting(string key)
    {
        using var connection = new SqliteConnection(_connectionString); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = "SELECT Value FROM AppSettings WHERE Key=$key"; command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar()?.ToString();
    }

    public void SetSetting(string key, string value)
    {
        using var connection = new SqliteConnection(_connectionString); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = "INSERT INTO AppSettings (Key,Value,UpdatedAt) VALUES ($key,$value,$at) ON CONFLICT(Key) DO UPDATE SET Value=$value,UpdatedAt=$at";
        command.Parameters.AddWithValue("$key", key); command.Parameters.AddWithValue("$value", value); command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O")); command.ExecuteNonQuery();
        AddAuditLog("SettingChanged", $"Key={key}");
    }

    public void AddAuditLog(string action, string details)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO AuditLogs (Action, Details, OccurredAt) VALUES ($action,$details,$occurred)";
        command.Parameters.AddWithValue("$action", action); command.Parameters.AddWithValue("$details", details); command.Parameters.AddWithValue("$occurred", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }
}
