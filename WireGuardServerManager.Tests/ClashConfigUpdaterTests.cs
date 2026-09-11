using System.Text;
using WireGuardServerManager.Core;

namespace WireGuardServerManager.Tests;

public sealed class ClashConfigUpdaterTests
{
    [Fact]
    public void UpdatesEveryMatchingWireGuardNodeAndCreatesBackup()
    {
        var path = CreateConfig("""
            mixed-port: 7890
            proxies:
              - name: phone
                type: wireguard
                server: 192.0.2.10
                port: 51820
                private-key: secret-is-never-logged
              - name: laptop
                type: wireguard
                server: "192.0.2.10"
                port: 51821 # stale
              - name: other
                type: wireguard
                server: 192.0.2.11
                port: 60000
            proxy-groups:
              - name: select
                type: select
                proxies: [phone, laptop]
            """);
        try
        {
            var result = ClashConfigUpdater.UpdateWireGuardPort(path, "192.0.2.10", 51822);
            var updated = File.ReadAllText(path);
            Assert.Equal(2, result.MatchedNodes);
            Assert.Equal(2, result.ChangedNodes);
            Assert.NotNull(result.BackupPath);
            Assert.True(File.Exists(result.BackupPath));
            Assert.Equal(2, Count(updated, "port: 51822"));
            Assert.Contains("port: 60000", updated);
            Assert.Contains("private-key: secret-is-never-logged", updated);
        }
        finally { DeleteConfigAndBackups(path); }
    }

    [Fact]
    public void SupportsBracketedIpv6AndPreservesBomAndLineEndings()
    {
        var path = Path.Combine(Path.GetTempPath(), $"clash-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, "proxies:\r\n  - name: ipv6\r\n    type: wireguard\r\n    server: '2001:db8::1'\r\n    port: 51820\r\n", new UTF8Encoding(true));
        try
        {
            var result = ClashConfigUpdater.UpdateWireGuardPort(path, "[2001:db8::1]", 51830);
            var bytes = File.ReadAllBytes(path);
            Assert.Equal(1, result.ChangedNodes);
            Assert.True(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
            Assert.Contains("\r\n", File.ReadAllText(path));
        }
        finally { DeleteConfigAndBackups(path); }
    }

    [Fact]
    public void DoesNotModifyFileWhenNoMatchingNodeExists()
    {
        var path = CreateConfig("proxies:\n  - name: other\n    type: socks5\n    server: 192.0.2.10\n    port: 1080\n");
        var before = File.ReadAllBytes(path);
        try
        {
            var result = ClashConfigUpdater.UpdateWireGuardPort(path, "192.0.2.10", 51822);
            Assert.Equal(0, result.MatchedNodes);
            Assert.Equal(0, result.ChangedNodes);
            Assert.Null(result.BackupPath);
            Assert.Equal(before, File.ReadAllBytes(path));
        }
        finally { DeleteConfigAndBackups(path); }
    }

    [Fact]
    public void DirectoryUpdateScansNestedYamlAndMatchesOnlyRequestedServer()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"clash-dir-{Guid.NewGuid():N}");
        var nested = Path.Combine(directory, "profiles");
        Directory.CreateDirectory(nested);
        var first = Path.Combine(directory, "first.yaml");
        var second = Path.Combine(nested, "second.yml");
        var unrelated = Path.Combine(directory, "notes.txt");
        File.WriteAllText(first, "proxies:\n  - name: one\n    type: wireguard\n    server: 192.0.2.10\n    port: 51820\n");
        File.WriteAllText(second, "proxies:\n  - name: two\n    type: wireguard\n    server: 192.0.2.10\n    port: 51821\n  - name: other\n    type: wireguard\n    server: 192.0.2.11\n    port: 60000\n");
        File.WriteAllText(unrelated, "server: 192.0.2.10\nport: 51820\n");
        try
        {
            var result = ClashConfigUpdater.UpdateWireGuardPortsInDirectory(directory, "192.0.2.10", 51830);
            Assert.Equal(2, result.ScannedFiles);
            Assert.Equal(2, result.MatchedFiles);
            Assert.Equal(2, result.MatchedNodes);
            Assert.Equal(2, result.ChangedNodes);
            Assert.Equal(2, result.BackupPaths.Count);
            Assert.Empty(result.Errors);
            Assert.Contains("port: 51830", File.ReadAllText(first));
            Assert.Contains("port: 51830", File.ReadAllText(second));
            Assert.Contains("port: 60000", File.ReadAllText(second));
            Assert.Contains("port: 51820", File.ReadAllText(unrelated));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static string CreateConfig(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"clash-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    private static int Count(string value, string fragment) => value.Split(fragment, StringSplitOptions.None).Length - 1;

    private static void DeleteConfigAndBackups(string path)
    {
        if (File.Exists(path)) File.Delete(path);
        foreach (var backup in Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".wg-manager-*.bak")) File.Delete(backup);
    }
}
