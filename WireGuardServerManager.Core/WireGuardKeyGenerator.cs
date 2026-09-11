using System.Diagnostics;

namespace WireGuardServerManager.Core;

public sealed record WireGuardKeyPair(string PrivateKey, string PublicKey);

public static class WireGuardKeyGenerator
{
    public static WireGuardKeyPair Generate()
    {
        var executable = FindWireGuardExecutable();
        var privateKey = Run(executable, "genkey", null).Trim();
        var publicKey = Run(executable, "pubkey", privateKey + Environment.NewLine).Trim();
        if (privateKey.Length == 0 || publicKey.Length == 0) throw new InvalidOperationException("wg.exe 未返回有效密钥。");
        return new WireGuardKeyPair(privateKey, publicKey);
    }

    private static string FindWireGuardExecutable()
    {
        var candidates = new[] { "wg.exe", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WireGuard", "wg.exe") };
        foreach (var candidate in candidates)
        {
            if (candidate != "wg.exe" && !File.Exists(candidate)) continue;
            try { Run(candidate, "--version", null); return candidate; } catch { }
        }
        throw new InvalidOperationException("未找到官方 WireGuard wg.exe，请先安装 WireGuard 客户端。");
    }

    private static string Run(string executable, string arguments, string? standardInput)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo
        {
            FileName = executable, Arguments = arguments, UseShellExecute = false,
            RedirectStandardInput = standardInput is not null, RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true
        }};
        process.Start();
        if (standardInput is not null) { process.StandardInput.Write(standardInput); process.StandardInput.Close(); }
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException($"wg.exe 执行失败：{error.Trim()}");
        return output;
    }
}
