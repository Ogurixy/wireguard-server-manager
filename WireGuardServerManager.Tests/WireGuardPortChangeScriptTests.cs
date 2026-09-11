using WireGuardServerManager.Core;

namespace WireGuardServerManager.Tests;

public sealed class WireGuardPortChangeScriptTests
{
    [Fact]
    public void BuildUsesSafeOrderAndCreatesBackup()
    {
        var script = WireGuardPortChangeScript.Build("wg0", 51895);

        Assert.Contains("ufw allow \"$new_port/udp\"", script);
        Assert.Contains("/etc/wireguard/backups", script);
        Assert.Contains("systemctl daemon-reload", script);
        Assert.Contains("systemctl restart \"wg-quick@$iface\"", script);
        Assert.Contains("actual=$($priv wg show", script);
        Assert.Contains("ufw --force delete allow \"$old_port/udp\"", script);
        Assert.True(script.IndexOf("ufw allow", StringComparison.Ordinal) < script.IndexOf("systemctl restart", StringComparison.Ordinal));
        Assert.True(script.IndexOf("systemctl daemon-reload", StringComparison.Ordinal) < script.IndexOf("systemctl restart", StringComparison.Ordinal));
        Assert.True(script.LastIndexOf("ufw --force delete allow \"$old_port/udp\"", StringComparison.Ordinal) > script.IndexOf("actual=$($priv wg show", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("wg0;reboot", 51895)]
    [InlineData("wg0", 0)]
    [InlineData("wg0", 65536)]
    public void BuildRejectsInvalidInput(string wireGuardInterface, int port)
        => Assert.ThrowsAny<ArgumentException>(() => WireGuardPortChangeScript.Build(wireGuardInterface, port));

    [Fact]
    public void ParseResultReturnsDetectedOldPortAndFirewallState()
    {
        var result = WireGuardPortChangeScript.ParseResult("noise\nWG_MANAGER_PORT_RESULT:51894:51895:managed:1\n");

        Assert.Equal(51894, result.OldPort);
        Assert.Equal(51895, result.NewPort);
        Assert.True(result.FirewallManaged);
        Assert.True(result.Changed);
    }

    [Theory]
    [InlineData("WG_MANAGER_PORT_RESULT:51894:51895:unknown:1")]
    [InlineData("WG_MANAGER_PORT_RESULT:51894:51895:managed:yes")]
    public void ParseResultRejectsUnknownState(string output)
        => Assert.Throws<InvalidOperationException>(() => WireGuardPortChangeScript.ParseResult(output));
}
