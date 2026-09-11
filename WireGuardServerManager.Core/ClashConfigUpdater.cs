using System.Text;
using System.Text.RegularExpressions;

namespace WireGuardServerManager.Core;

public sealed record ClashConfigUpdateResult(int MatchedNodes, int ChangedNodes, string? BackupPath);
public sealed record ClashDirectoryUpdateResult(
    int ScannedFiles,
    int MatchedFiles,
    int MatchedNodes,
    int ChangedNodes,
    IReadOnlyList<string> BackupPaths,
    IReadOnlyList<string> Errors);

public static partial class ClashConfigUpdater
{
    public static ClashDirectoryUpdateResult UpdateWireGuardPortsInDirectory(string configDirectory, string serverHost, int newPort)
    {
        if (string.IsNullOrWhiteSpace(configDirectory)) throw new ArgumentException("Clash config directory is required.", nameof(configDirectory));
        if (!Directory.Exists(configDirectory)) throw new DirectoryNotFoundException("Clash configuration directory was not found.");

        var files = Directory.EnumerateFiles(configDirectory, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path).Equals(".yaml", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".yml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var matchedFiles = 0;
        var matchedNodes = 0;
        var changedNodes = 0;
        var backups = new List<string>();
        var errors = new List<string>();
        foreach (var file in files)
        {
            try
            {
                var result = UpdateWireGuardPort(file, serverHost, newPort);
                if (result.MatchedNodes > 0) matchedFiles++;
                matchedNodes += result.MatchedNodes;
                changedNodes += result.ChangedNodes;
                if (result.BackupPath is not null) backups.Add(result.BackupPath);
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetRelativePath(configDirectory, file)}: {ex.Message}");
            }
        }

        return new ClashDirectoryUpdateResult(files.Length, matchedFiles, matchedNodes, changedNodes, backups, errors);
    }

    public static ClashConfigUpdateResult UpdateWireGuardPort(string configPath, string serverHost, int newPort)
    {
        if (string.IsNullOrWhiteSpace(configPath)) throw new ArgumentException("Clash config path is required.", nameof(configPath));
        if (!File.Exists(configPath)) throw new FileNotFoundException("Clash configuration file was not found.", configPath);
        if (string.IsNullOrWhiteSpace(serverHost)) throw new ArgumentException("Server host is required.", nameof(serverHost));
        if (newPort is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(newPort));

        var extension = Path.GetExtension(configPath);
        if (!extension.Equals(".yaml", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".yml", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Clash configuration must be a .yaml or .yml file.");

        var originalBytes = File.ReadAllBytes(configPath);
        var hasBom = originalBytes.AsSpan().StartsWith(Encoding.UTF8.Preamble);
        var text = Encoding.UTF8.GetString(hasBom ? originalBytes.AsSpan(Encoding.UTF8.Preamble.Length) : originalBytes);
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var endsWithNewline = text.EndsWith("\n", StringComparison.Ordinal);
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        if (endsWithNewline && lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);

        var matched = 0;
        var changed = 0;
        foreach (var (start, end) in FindProxyBlocks(lines))
        {
            string? type = null;
            string? host = null;
            var portLine = -1;
            var existingPort = -1;
            for (var i = start; i < end; i++)
            {
                if (!TryReadProperty(lines[i], out var key, out var value)) continue;
                if (key.Equals("type", StringComparison.OrdinalIgnoreCase)) type = value;
                else if (key.Equals("server", StringComparison.OrdinalIgnoreCase)) host = value;
                else if (key.Equals("port", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out var parsedPort))
                {
                    portLine = i;
                    existingPort = parsedPort;
                }
            }

            if (!string.Equals(type, "wireguard", StringComparison.OrdinalIgnoreCase) || !HostsEqual(host, serverHost)) continue;
            matched++;
            if (portLine < 0) throw new InvalidOperationException("A matching WireGuard node does not contain a numeric port field.");
            if (existingPort == newPort) continue;
            lines[portLine] = PortLineRegex().Replace(lines[portLine], $"${{prefix}}{newPort}${{suffix}}");
            changed++;
        }

        if (matched == 0 || changed == 0) return new ClashConfigUpdateResult(matched, changed, null);

        var updatedText = string.Join(newline, lines) + (endsWithNewline ? newline : string.Empty);
        var contentBytes = new UTF8Encoding(false).GetBytes(updatedText);
        var updatedBytes = contentBytes;
        if (hasBom)
        {
            updatedBytes = new byte[Encoding.UTF8.Preamble.Length + contentBytes.Length];
            Encoding.UTF8.Preamble.CopyTo(updatedBytes);
            contentBytes.CopyTo(updatedBytes, Encoding.UTF8.Preamble.Length);
        }
        var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
        var backupPath = configPath + $".wg-manager-{timestamp}-{Guid.NewGuid():N}.bak";
        var temporaryPath = configPath + $".wg-manager-{Guid.NewGuid():N}.tmp";
        File.Copy(configPath, backupPath, false);
        try
        {
            File.WriteAllBytes(temporaryPath, updatedBytes);
            File.Move(temporaryPath, configPath, true);
        }
        catch
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            throw;
        }

        return new ClashConfigUpdateResult(matched, changed, backupPath);
    }

    private static IEnumerable<(int Start, int End)> FindProxyBlocks(IReadOnlyList<string> lines)
    {
        for (var sectionStart = 0; sectionStart < lines.Count; sectionStart++)
        {
            if (!IsProxiesHeader(lines[sectionStart], out var sectionIndent)) continue;
            var sectionEnd = sectionStart + 1;
            while (sectionEnd < lines.Count)
            {
                var trimmed = lines[sectionEnd].Trim();
                if (trimmed.Length > 0 && !trimmed.StartsWith('#') && LeadingSpaces(lines[sectionEnd]) <= sectionIndent) break;
                sectionEnd++;
            }

            var itemIndent = -1;
            for (var i = sectionStart + 1; i < sectionEnd; i++)
            {
                if (lines[i].TrimStart().StartsWith("- ", StringComparison.Ordinal))
                {
                    itemIndent = LeadingSpaces(lines[i]);
                    break;
                }
            }
            if (itemIndent < 0) continue;

            var starts = Enumerable.Range(sectionStart + 1, sectionEnd - sectionStart - 1)
                .Where(i => LeadingSpaces(lines[i]) == itemIndent && lines[i].TrimStart().StartsWith("- ", StringComparison.Ordinal))
                .ToArray();
            for (var i = 0; i < starts.Length; i++) yield return (starts[i], i + 1 < starts.Length ? starts[i + 1] : sectionEnd);
            sectionStart = sectionEnd - 1;
        }
    }

    private static bool IsProxiesHeader(string line, out int indent)
    {
        indent = LeadingSpaces(line);
        return Regex.IsMatch(line, "^\\s*proxies\\s*:\\s*(?:#.*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool TryReadProperty(string line, out string key, out string value)
    {
        key = string.Empty;
        value = string.Empty;
        var match = PropertyRegex().Match(line);
        if (!match.Success) return false;
        key = match.Groups["key"].Value;
        value = Unquote(StripComment(match.Groups["value"].Value).Trim());
        return true;
    }

    private static string StripComment(string value)
    {
        char quote = '\0';
        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];
            if (quote == '\0' && current is '\'' or '"') quote = current;
            else if (quote == current) quote = '\0';
            else if (quote == '\0' && current == '#' && (i == 0 || char.IsWhiteSpace(value[i - 1]))) return value[..i];
        }
        return value;
    }

    private static string Unquote(string value) => value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')) ? value[1..^1] : value;
    private static bool HostsEqual(string? left, string right) => string.Equals(NormalizeHost(left), NormalizeHost(right), StringComparison.OrdinalIgnoreCase);
    private static string NormalizeHost(string? value) => (value ?? string.Empty).Trim().TrimStart('[').TrimEnd(']');
    private static int LeadingSpaces(string value) => value.TakeWhile(char.IsWhiteSpace).Count();

    [GeneratedRegex("^\\s*(?:-\\s*)?(?<key>[A-Za-z0-9_-]+)\\s*:\\s*(?<value>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex PropertyRegex();

    [GeneratedRegex("^(?<prefix>\\s*(?:-\\s*)?port\\s*:\\s*)['\"]?\\d+['\"]?(?<suffix>\\s*(?:#.*)?)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PortLineRegex();
}
