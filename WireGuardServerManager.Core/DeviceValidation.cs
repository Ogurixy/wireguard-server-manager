using System.Net;
using System.Net.Sockets;

namespace WireGuardServerManager.Core;

public static class DeviceValidation
{
    public static string? NormalizeIpv4Subnet(string? value) => TryParseSubnet(value, out var network, out var prefix) ? $"{network}/{prefix}" : null;

    public static string? Validate(string name, string publicKey, string vpnIp, Server server, IReadOnlyCollection<Device> existing)
    {
        if (string.IsNullOrWhiteSpace(name)) return "设备名称不能为空。";
        if (string.IsNullOrWhiteSpace(publicKey)) return "PublicKey 不能为空。";
        if (!IsValidWireGuardKey(publicKey)) return "PublicKey 格式不正确，应为 WireGuard Base64 公钥。";
        if (!IPAddress.TryParse(vpnIp, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork) return "VPN IP 必须是有效的 IPv4 地址。";
        if (!IsInSubnet(ip, server.VpnSubnet)) return $"VPN IP 不在服务器网段 {server.VpnSubnet} 内。";
        if (IsNetworkOrBroadcast(ip, server.VpnSubnet)) return "不能使用网络地址或广播地址。";
        if (existing.Any(d => d.ServerId == server.Id && d.VpnIp == vpnIp)) return "该 VPN IP 已被设备占用。";
        if (existing.Any(d => d.PublicKey == publicKey)) return "该 PublicKey 已被使用。";
        return null;
    }

    public static string? FindNextIp(Server server, IEnumerable<Device> devices, IEnumerable<string>? serverAllowedIps = null)
    {
        if (!TryParseSubnet(server.VpnSubnet, out var network, out var prefix) || prefix > 30) return null;
        var used = devices.Where(d => d.ServerId == server.Id).Select(d => d.VpnIp).ToHashSet(StringComparer.Ordinal);
        var remoteAllowed = serverAllowedIps?.ToArray() ?? Array.Empty<string>();
        var networkValue = (ulong)ToUInt32(network);
        var hostCount = 1UL << (32 - prefix);
        var first = networkValue + 2; // .1 is reserved for the server.
        var last = networkValue + hostCount - 2; // broadcast excluded.
        for (var candidate = first; candidate <= last; candidate++)
        {
            var value = FromUInt32((uint)candidate).ToString();
            if (!used.Contains(value) && !IsVpnIpReservedByAllowedIps(value, remoteAllowed)) return value;
        }
        return null;
    }

    public static bool IsVpnIpReservedByAllowedIps(string vpnIp, IEnumerable<string> allowedIps)
    {
        if (!IPAddress.TryParse(vpnIp, out var candidate) || candidate.AddressFamily != AddressFamily.InterNetwork) return true;
        var candidateValue = ToUInt32(candidate);
        foreach (var item in allowedIps.SelectMany(x => x.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
        {
            var parts = item.Split('/');
            if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var network) || network.AddressFamily != AddressFamily.InterNetwork || !int.TryParse(parts[1], out var prefix) || prefix is < 0 or > 32)
                continue;
            var mask = Mask(prefix);
            if ((candidateValue & mask) == (ToUInt32(network) & mask)) return true;
        }
        return false;
    }

    private static bool IsValidWireGuardKey(string value) => Convert.TryFromBase64String(value.Trim(), new byte[32], out var written) && written == 32;

    private static bool IsInSubnet(IPAddress ip, string? subnet) => TryParseSubnet(subnet, out var network, out var prefix) && (ToUInt32(ip) & Mask(prefix)) == (ToUInt32(network) & Mask(prefix));

    private static bool IsNetworkOrBroadcast(IPAddress ip, string? subnet)
    {
        if (!TryParseSubnet(subnet, out var network, out var prefix)) return true;
        var value = ToUInt32(ip); var net = ToUInt32(network); var broadcast = net + ((1u << (32 - prefix)) - 1);
        return value == net || value == broadcast || value == net + 1;
    }

    private static bool TryParseSubnet(string? value, out IPAddress network, out int prefix)
    {
        network = IPAddress.None; prefix = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var parts = value.Split('/');
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var parsed) || parsed.AddressFamily != AddressFamily.InterNetwork || !int.TryParse(parts[1], out prefix) || prefix is < 0 or > 30) return false;
        network = FromUInt32(ToUInt32(parsed) & Mask(prefix));
        return true;
    }

    private static uint ToUInt32(IPAddress ip) => BitConverter.ToUInt32(ip.GetAddressBytes().Reverse().ToArray());
    private static IPAddress FromUInt32(uint value) => new(BitConverter.GetBytes(value).Reverse().ToArray());
    private static uint Mask(int prefix) => prefix == 0 ? 0 : uint.MaxValue << (32 - prefix);
}
