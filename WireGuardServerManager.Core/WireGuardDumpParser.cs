using System.Globalization;
using System.Net;

namespace WireGuardServerManager.Core;

public sealed record ParsedWireGuardDump(
    IReadOnlyList<WireGuardPeerSnapshot> Peers,
    IReadOnlyList<string> Interfaces,
    IReadOnlyDictionary<string, int> ListenPorts);

public static class WireGuardDumpParser
{
    public static ParsedWireGuardDump Parse(string dump, DateTimeOffset now, TimeSpan onlineThreshold)
    {
        var interfaces = new HashSet<string>(StringComparer.Ordinal);
        var listenPorts = new Dictionary<string, int>(StringComparer.Ordinal);
        var peers = new List<WireGuardPeerSnapshot>();

        foreach (var raw in dump.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = raw.Split('\t');
            if (fields.Length == 5)
            {
                interfaces.Add(fields[0]);
                if (int.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var listenPort) && listenPort is >= 1 and <= 65535)
                    listenPorts[fields[0]] = listenPort;
                continue;
            }

            if (fields.Length < 9) continue;
            interfaces.Add(fields[0]);
            var endpoint = EndpointParser.Parse(fields[3]);
            var handshake = ParseUnixSeconds(fields[5]);
            var rx = ParseLong(fields[6]);
            var tx = ParseLong(fields[7]);
            var keepalive = ParseInt(fields[8]);
            peers.Add(new WireGuardPeerSnapshot(
                fields[0], fields[1], NullIfNone(fields[3]), endpoint.PublicIp, endpoint.Port,
                NullIfNone(fields[4]) ?? string.Empty, handshake, rx, tx, keepalive,
                OnlineStatus.IsOnline(handshake, now, onlineThreshold), null, false));
        }

        return new ParsedWireGuardDump(peers, interfaces.OrderBy(x => x).ToArray(), listenPorts);
    }

    private static DateTimeOffset? ParseUnixSeconds(string value)
    {
        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) || seconds <= 0) return null;
        return DateTimeOffset.FromUnixTimeSeconds(seconds);
    }

    private static long ParseLong(string value) => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : 0;
    private static int? ParseInt(string value) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;
    private static string? NullIfNone(string value) => string.IsNullOrWhiteSpace(value) || value == "(none)" ? null : value;
}

public static class EndpointParser
{
    public static (string? PublicIp, int? Port) Parse(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint) || endpoint == "(none)") return (null, null);
        endpoint = endpoint.Trim();
        if (endpoint.StartsWith("[", StringComparison.Ordinal))
        {
            var close = endpoint.IndexOf(']');
            if (close > 1 && close + 2 <= endpoint.Length && endpoint[close + 1] == ':' && int.TryParse(endpoint[(close + 2)..], out var port))
                return (endpoint[1..close], port);
            return (endpoint.Trim('[', ']'), null);
        }

        var separator = endpoint.LastIndexOf(':');
        if (separator > 0 && int.TryParse(endpoint[(separator + 1)..], out var ipv4Port) && IPAddress.TryParse(endpoint[..separator], out _))
            return (endpoint[..separator], ipv4Port);
        return (endpoint, null);
    }
}
