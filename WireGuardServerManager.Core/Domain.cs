namespace WireGuardServerManager.Core;

public sealed record Server(Guid Id, string Name, string Host, int SshPort, string SshUser, string WireGuardInterface, string? VpnSubnet, int WireGuardPort, bool Enabled = true);

public sealed record Device(Guid Id, Guid ServerId, string Name, string PublicKey, string VpnIp, string UserName, bool Enabled = true);

public sealed record WireGuardPeerSnapshot(
    string Interface,
    string PublicKey,
    string? Endpoint,
    string? PublicIp,
    int? EndpointPort,
    string AllowedIps,
    DateTimeOffset? LatestHandshake,
    long TransferRx,
    long TransferTx,
    int? PersistentKeepalive,
    bool IsOnline,
    Device? MatchedDevice,
    bool IsUnknown);

public sealed record SecurityEvent(string Type, string Description, DateTimeOffset OccurredAt, Guid? ServerId = null, string? PublicKey = null);

public sealed record ReconciliationResult(
    IReadOnlyList<WireGuardPeerSnapshot> Peers,
    IReadOnlyList<Device> DatabaseOnly,
    IReadOnlyList<WireGuardPeerSnapshot> UnknownPeers,
    IReadOnlyList<string> Conflicts);

public sealed record DashboardSummary(int Servers, int HealthyServers, int TotalUsers, int TotalDevices, int OnlineDevices, int UnknownPeers, long TodayTrafficBytes, long MonthlyTrafficBytes, int SecurityEvents);

public sealed record ConnectionSession(Guid Id, Guid DeviceId, Guid ServerId, string? PublicIp, string VpnIp, DateTimeOffset ConnectedAt, DateTimeOffset? DisconnectedAt, DateTimeOffset? LastHandshake, long Rx, long Tx);

public sealed record TrafficSnapshot(Guid ServerId, Guid? DeviceId, string PublicKey, string? VpnIp, DateTimeOffset CapturedAt, long Rx, long Tx, bool CounterReset);

public sealed record TrafficTotal(Guid ServerId, Guid? DeviceId, string PublicKey, long RxTotal, long TxTotal, DateTimeOffset UpdatedAt);

public sealed record AuditLog(long Id, string Action, string Details, DateTimeOffset OccurredAt);

public sealed record UserRecord(Guid Id, string Name, bool Enabled, DateTimeOffset CreatedAt);

public sealed record AccessRule(Guid Id, string Type, string Value, string Description, bool Enabled, DateTimeOffset CreatedAt);

public sealed record IgnoredPeer(Guid ServerId, string PublicKey, DateTimeOffset IgnoredAt);

public static class OnlineStatus
{
    public static bool IsOnline(DateTimeOffset? latestHandshake, DateTimeOffset now, TimeSpan threshold) =>
        latestHandshake is not null && now - latestHandshake.Value <= threshold && now >= latestHandshake.Value;
}
