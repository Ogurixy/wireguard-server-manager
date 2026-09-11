using WireGuardServerManager.Core;

namespace WireGuardServerManager.Tests;

public sealed class SshCommandResultTests
{
    [Fact]
    public void SuccessfulCommandMayWriteWarningToStandardError()
    {
        SshWireGuardClient.ThrowIfCommandFailed(0, "harmless sudo warning");
    }

    [Fact]
    public void FailedCommandIncludesExitStatusWhenStandardErrorIsEmpty()
    {
        var error = Assert.Throws<InvalidOperationException>(() => SshWireGuardClient.ThrowIfCommandFailed(17, string.Empty));
        Assert.Contains("17", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FailedCommandRedactsPrivateKeyLabel()
    {
        var error = Assert.Throws<InvalidOperationException>(() => SshWireGuardClient.ThrowIfCommandFailed(1, "PrivateKey must not be logged"));
        Assert.DoesNotContain("PrivateKey", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
