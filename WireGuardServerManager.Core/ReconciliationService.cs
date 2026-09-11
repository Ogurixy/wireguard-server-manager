namespace WireGuardServerManager.Core;

public sealed class ReconciliationService
{
    public ReconciliationResult Reconcile(Guid serverId, ParsedWireGuardDump parsed, IReadOnlyList<Device> devices)
    {
        var serverDevices = devices.Where(d => d.ServerId == serverId).ToList();
        var conflicts = new List<string>();
        foreach (var group in serverDevices.GroupBy(d => d.PublicKey, StringComparer.Ordinal).Where(g => g.Count() > 1))
            conflicts.Add($"PublicKey conflict: {group.Key} is assigned to {group.Count()} database devices.");
        var byKey = serverDevices.GroupBy(d => d.PublicKey, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var peers = new List<WireGuardPeerSnapshot>();
        var unknown = new List<WireGuardPeerSnapshot>();

        foreach (var peer in parsed.Peers)
        {
            if (byKey.TryGetValue(peer.PublicKey, out var device))
            {
                var matched = peer with { MatchedDevice = device, IsUnknown = false };
                peers.Add(matched);
                if (!device.Enabled) unknown.Add(matched);
            }
            else
            {
                var unknownPeer = peer with { IsUnknown = true };
                peers.Add(unknownPeer);
                unknown.Add(unknownPeer);
            }
        }

        var activeKeys = parsed.Peers.Select(p => p.PublicKey).ToHashSet(StringComparer.Ordinal);
        var databaseOnly = serverDevices.Where(d => d.Enabled && !activeKeys.Contains(d.PublicKey)).ToArray();
        var allowedIpOwners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var peer in peers)
        {
            foreach (var ip in peer.AllowedIps.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (allowedIpOwners.TryGetValue(ip, out var existing)) conflicts.Add($"VPN IP conflict: {ip} is used by {existing} and {peer.PublicKey}.");
                else allowedIpOwners[ip] = peer.PublicKey;
            }
            if (peer.MatchedDevice is { } matched && !string.IsNullOrWhiteSpace(matched.VpnIp) && !peer.AllowedIps.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(matched.VpnIp + "/32", StringComparer.Ordinal))
                conflicts.Add($"VPN IP mismatch: device {matched.PublicKey} expects {matched.VpnIp}/32 but server has {peer.AllowedIps}.");
        }
        return new ReconciliationResult(peers, databaseOnly, unknown, conflicts.Distinct(StringComparer.Ordinal).ToArray());
    }
}
