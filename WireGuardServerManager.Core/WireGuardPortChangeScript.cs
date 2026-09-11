namespace WireGuardServerManager.Core;

public sealed record WireGuardPortChangeResult(int OldPort, int NewPort, bool FirewallManaged, bool Changed);

public static class WireGuardPortChangeScript
{
    private const string ResultPrefix = "WG_MANAGER_PORT_RESULT:";

    public static string Build(string wireGuardInterface, int newPort)
    {
        ValidateInterface(wireGuardInterface);
        ValidatePort(newPort);

        var iface = Quote(wireGuardInterface);
        return string.Join("; ", new[]
        {
            "set -eu",
            $"iface={iface}",
            $"new_port={newPort}",
            "conf=\"/etc/wireguard/$iface.conf\"",
            "priv=''",
            "if [ \"$(id -u)\" -ne 0 ]; then priv=sudo; fi",
            "[ -f \"$conf\" ] || { echo CONFIG_MISSING >&2; exit 20; }",
            "old_port=$($priv wg show \"$iface\" listen-port)",
            "case \"$old_port\" in ''|*[!0-9]*) echo INVALID_CURRENT_PORT >&2; exit 21;; esac",
            "firewall=not-installed",
            "if command -v ufw >/dev/null 2>&1; then firewall=managed; $priv ufw allow \"$new_port/udp\" >/dev/null; fi",
            "if [ \"$old_port\" = \"$new_port\" ]; then echo \"WG_MANAGER_PORT_RESULT:$old_port:$new_port:$firewall:0\"; exit 0; fi",
            "$priv install -d -m 700 /etc/wireguard/backups",
            "backup=\"/etc/wireguard/backups/$iface.port-$(date -u +%Y%m%d%H%M%S).conf\"",
            "$priv cp -p \"$conf\" \"$backup\"",
            "tmp=$(mktemp)",
            "if ! $priv awk -v port=\"$new_port\" 'BEGIN{done=0} /^[[:space:]]*ListenPort[[:space:]]*=/ && !done { print \"ListenPort = \" port; done=1; next } { print } END { if (!done) exit 42 }' \"$conf\" > \"$tmp\"; then rm -f \"$tmp\"; [ \"$firewall\" = managed ] && $priv ufw --force delete allow \"$new_port/udp\" >/dev/null 2>&1 || true; echo LISTEN_PORT_NOT_FOUND >&2; exit 22; fi",
            "$priv install -m 600 \"$tmp\" \"$conf\"",
            "rm -f \"$tmp\"",
            "$priv systemctl daemon-reload",
            "if ! $priv systemctl restart \"wg-quick@$iface\"; then $priv cp -p \"$backup\" \"$conf\"; $priv systemctl restart \"wg-quick@$iface\" || true; [ \"$firewall\" = managed ] && $priv ufw --force delete allow \"$new_port/udp\" >/dev/null 2>&1 || true; echo RESTART_FAILED_ROLLED_BACK >&2; exit 23; fi",
            "actual=$($priv wg show \"$iface\" listen-port)",
            "if [ \"$actual\" != \"$new_port\" ]; then $priv cp -p \"$backup\" \"$conf\"; $priv systemctl restart \"wg-quick@$iface\" || true; [ \"$firewall\" = managed ] && $priv ufw --force delete allow \"$new_port/udp\" >/dev/null 2>&1 || true; echo PORT_VERIFY_FAILED_ROLLED_BACK >&2; exit 24; fi",
            "if [ \"$firewall\" = managed ]; then $priv ufw --force delete allow \"$old_port/udp\" >/dev/null 2>&1 || true; fi",
            "echo \"WG_MANAGER_PORT_RESULT:$old_port:$new_port:$firewall:1\""
        });
    }

    public static WireGuardPortChangeResult ParseResult(string output)
    {
        var resultLine = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(line => line.StartsWith(ResultPrefix, StringComparison.Ordinal));
        if (resultLine is null) throw new InvalidOperationException("The VPS did not return a valid WireGuard port-change result.");

        var parts = resultLine[ResultPrefix.Length..].Split(':');
        if (parts.Length != 4 || !int.TryParse(parts[0], out var oldPort) || !int.TryParse(parts[1], out var newPort))
            throw new InvalidOperationException("The VPS returned a malformed WireGuard port-change result.");
        if (parts[2] is not ("managed" or "not-installed") || parts[3] is not ("0" or "1"))
            throw new InvalidOperationException("The VPS returned an invalid WireGuard port-change state.");
        ValidatePort(oldPort);
        ValidatePort(newPort);
        return new WireGuardPortChangeResult(oldPort, newPort, parts[2] == "managed", parts[3] == "1");
    }

    private static void ValidateInterface(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(c => !(char.IsLetterOrDigit(c) || c is '_' or '-' or '.')))
            throw new ArgumentException("Invalid WireGuard interface.", nameof(value));
    }

    private static void ValidatePort(int port)
    {
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port), "WireGuard port must be between 1 and 65535.");
    }

    private static string Quote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
}
