using Renci.SshNet;

namespace WireGuardServerManager.Core;

public sealed record SshConnectionOptions(string Host, int Port, string UserName, string PrivateKeyPath, string? PrivateKeyPassphrase = null);

public sealed class SshWireGuardClient
{
    public Task<string> ExecuteReadOnlyAsync(SshConnectionOptions options, string command, CancellationToken cancellationToken = default)
    {
        if (command is not ("wg show all dump" or "wg show" or "wg show interfaces"))
            throw new ArgumentException("Only approved WireGuard read commands are allowed.", nameof(command));
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var key = string.IsNullOrEmpty(options.PrivateKeyPassphrase)
                ? new PrivateKeyFile(options.PrivateKeyPath)
                : new PrivateKeyFile(options.PrivateKeyPath, options.PrivateKeyPassphrase);
            using var client = CreateClient(options, key);
            client.Connect();
            var result = client.RunCommand(command);
            client.Disconnect();
            ThrowIfCommandFailed(result.ExitStatus, result.Error);
            return result.Result;
        }, cancellationToken);
    }

    public Task<string> GetServerPublicKeyAsync(SshConnectionOptions options, string wireGuardInterface, CancellationToken cancellationToken = default)
        => RunApprovedAsync(options, $"wg show {QuoteIdentifier(wireGuardInterface)} public-key", cancellationToken);

    public Task<string> GetInterfaceIpv4CidrAsync(SshConnectionOptions options, string wireGuardInterface, CancellationToken cancellationToken = default)
    {
        ValidateInterface(wireGuardInterface);
        return RunApprovedAsync(options, $"ip -o -4 addr show dev {QuoteIdentifier(wireGuardInterface)} scope global | awk 'NR == 1 {{ print $4 }}'", cancellationToken);
    }

    public async Task<WireGuardPortChangeResult> ChangeWireGuardPortAsync(SshConnectionOptions options, string wireGuardInterface, int newPort, CancellationToken cancellationToken = default)
    {
        var output = await RunApprovedAsync(options, WireGuardPortChangeScript.Build(wireGuardInterface, newPort), cancellationToken).ConfigureAwait(false);
        return WireGuardPortChangeScript.ParseResult(output);
    }

    public Task AddPeerAsync(SshConnectionOptions options, string wireGuardInterface, string publicKey, string vpnIp, CancellationToken cancellationToken = default)
    {
        ValidateWireGuardInputs(wireGuardInterface, publicKey, vpnIp);
        var iface = QuoteIdentifier(wireGuardInterface);
        var key = QuoteIdentifier(publicKey);
        var ip = QuoteIdentifier(vpnIp);
        var command = $"set -eu; conf=/etc/wireguard/{iface}.conf; test -f \"$conf\"; install -d -m 700 /etc/wireguard/backups; backup=/etc/wireguard/backups/{iface}.$(date -u +%Y%m%d%H%M%S).conf; cp -p \"$conf\" \"$backup\"; if wg show {iface} peers | grep -Fxq {key}; then echo PEER_EXISTS >&2; exit 17; fi; printf '\\n[Peer]\\n# managed-by-wg-manager\\nPublicKey = %s\\nAllowedIPs = %s/32\\n' {key} {ip} >> \"$conf\"; if ! wg set {iface} peer {key} allowed-ips {ip}/32; then cp -p \"$backup\" \"$conf\"; exit 1; fi; if ! wg show {iface} peers | grep -Fxq {key}; then cp -p \"$backup\" \"$conf\"; wg set {iface} peer {key} remove || true; exit 1; fi; echo PEER_ADDED";
        return RunApprovedAsync(options, command, cancellationToken);
    }

    public Task RemoveManagedPeerAsync(SshConnectionOptions options, string wireGuardInterface, string publicKey, CancellationToken cancellationToken = default)
    {
        ValidateWireGuardInputs(wireGuardInterface, publicKey, "10.0.0.1");
        var iface = QuoteIdentifier(wireGuardInterface);
        var key = QuoteIdentifier(publicKey);
        var keyLine = QuoteIdentifier("PublicKey = " + publicKey);
        var command = $"set -eu; conf=/etc/wireguard/{iface}.conf; test -f \"$conf\"; install -d -m 700 /etc/wireguard/backups; backup=/etc/wireguard/backups/{iface}.$(date -u +%Y%m%d%H%M%S).conf; cp -p \"$conf\" \"$backup\"; if ! wg show {iface} peers | grep -Fxq {key}; then echo PEER_MISSING >&2; exit 17; fi; old_allowed=$(wg show {iface} dump | awk -v peer={key} '$1 == peer {{print $4; exit}}'); wg set {iface} peer {key} remove; tmp=$(mktemp); if ! awk -v target={keyLine} 'function emit() {{ if (remove) matched=1; if (!remove) printf \"%s\", block; block=\"\"; remove=0; managed=0 }} /^\\[Peer\\]$/ {{ emit(); inpeer=1; block=$0 ORS; next }} inpeer {{ block=block $0 ORS; if ($0 == \"# managed-by-wg-manager\") managed=1; if (managed && $0 == target) remove=1; next }} {{ print }} END {{ if (inpeer) emit(); exit matched ? 0 : 1 }}' \"$conf\" > \"$tmp\"; then rm -f \"$tmp\"; cp -p \"$backup\" \"$conf\"; test -n \"$old_allowed\" && wg set {iface} peer {key} allowed-ips \"$old_allowed\"; echo REFUSED_UNMANAGED_PEER >&2; exit 18; fi; mv \"$tmp\" \"$conf\"; if wg show {iface} peers | grep -Fxq {key}; then cp -p \"$backup\" \"$conf\"; test -n \"$old_allowed\" && wg set {iface} peer {key} allowed-ips \"$old_allowed\"; exit 1; fi; echo PEER_REMOVED";
        return RunApprovedAsync(options, command, cancellationToken);
    }

    public Task RemovePeerAsync(SshConnectionOptions options, string wireGuardInterface, string publicKey, CancellationToken cancellationToken = default)
    {
        ValidateWireGuardInputs(wireGuardInterface, publicKey, "10.0.0.1");
        var iface = QuoteIdentifier(wireGuardInterface);
        var key = QuoteIdentifier(publicKey);
        var keyLine = QuoteIdentifier("PublicKey = " + publicKey);
        var command = $"set -eu; conf=/etc/wireguard/{iface}.conf; test -f \"$conf\"; install -d -m 700 /etc/wireguard/backups; backup=/etc/wireguard/backups/{iface}.$(date -u +%Y%m%d%H%M%S).conf; cp -p \"$conf\" \"$backup\"; if ! wg show {iface} peers | grep -Fxq {key}; then echo PEER_MISSING >&2; exit 17; fi; old_allowed=$(wg show {iface} dump | awk -v peer={key} '$1 == peer {{print $4; exit}}'); wg set {iface} peer {key} remove; tmp=$(mktemp); if ! awk -v target={keyLine} 'function emit() {{ if (remove) matched=1; if (!remove) printf \"%s\", block; block=\"\"; remove=0 }} /^\\[Peer\\]$/ {{ emit(); inpeer=1; block=$0 ORS; next }} inpeer {{ block=block $0 ORS; if ($0 == target) remove=1; next }} {{ print }} END {{ if (inpeer) emit(); exit matched ? 0 : 1 }}' \"$conf\" > \"$tmp\"; then rm -f \"$tmp\"; cp -p \"$backup\" \"$conf\"; test -n \"$old_allowed\" && wg set {iface} peer {key} allowed-ips \"$old_allowed\"; echo PEER_NOT_IN_CONFIG >&2; exit 18; fi; mv \"$tmp\" \"$conf\"; if wg show {iface} peers | grep -Fxq {key}; then cp -p \"$backup\" \"$conf\"; test -n \"$old_allowed\" && wg set {iface} peer {key} allowed-ips \"$old_allowed\"; exit 1; fi; echo PEER_REMOVED";
        return RunApprovedAsync(options, command, cancellationToken);
    }

    private Task<string> RunApprovedAsync(SshConnectionOptions options, string command, CancellationToken cancellationToken)
        => Task.Run(() => RunRemote(options, command, cancellationToken), cancellationToken);

    private static string RunRemote(SshConnectionOptions options, string command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var key = string.IsNullOrEmpty(options.PrivateKeyPassphrase)
            ? new PrivateKeyFile(options.PrivateKeyPath)
            : new PrivateKeyFile(options.PrivateKeyPath, options.PrivateKeyPassphrase);
        using var client = CreateClient(options, key);
        client.Connect();
        var result = client.RunCommand(command);
        client.Disconnect();
        ThrowIfCommandFailed(result.ExitStatus, result.Error);
        return result.Result.Trim();
    }

    private static SshClient CreateClient(SshConnectionOptions options, PrivateKeyFile key)
    {
        var connection = new ConnectionInfo(options.Host, options.Port, options.UserName, new PrivateKeyAuthenticationMethod(options.UserName, key))
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        return new SshClient(connection) { KeepAliveInterval = TimeSpan.FromSeconds(15) };
    }

    internal static void ThrowIfCommandFailed(int? exitStatus, string? standardError)
    {
        if (exitStatus == 0) return;
        var detail = string.IsNullOrWhiteSpace(standardError)
            ? exitStatus is null ? "SSH command did not return an exit status." : $"SSH command exited with code {exitStatus}."
            : Redact(standardError).Trim();
        throw new InvalidOperationException(detail);
    }

    private static void ValidateWireGuardInputs(string wireGuardInterface, string publicKey, string vpnIp)
    {
        ValidateInterface(wireGuardInterface);
        if (string.IsNullOrWhiteSpace(publicKey) || publicKey.Any(c => !(char.IsLetterOrDigit(c) || c is '+' or '/' or '='))) throw new ArgumentException("Invalid WireGuard public key.");
        if (!System.Net.IPAddress.TryParse(vpnIp, out _)) throw new ArgumentException("Invalid VPN IP.");
    }

    private static void ValidateInterface(string wireGuardInterface)
    {
        if (string.IsNullOrWhiteSpace(wireGuardInterface) || wireGuardInterface.Any(c => !(char.IsLetterOrDigit(c) || c is '_' or '-' or '.')))
            throw new ArgumentException("Invalid WireGuard interface.");
    }

    private static string QuoteIdentifier(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private static string Redact(string value) => value.Replace("PrivateKey", "[REDACTED]", StringComparison.OrdinalIgnoreCase);
}
