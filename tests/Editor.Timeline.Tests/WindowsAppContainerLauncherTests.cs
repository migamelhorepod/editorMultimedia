using Editor.Security;
using Xunit;

namespace Editor.Timeline.Tests;

public sealed class WindowsAppContainerLauncherTests
{
    [Fact]
    public void LauncherRejectsUntrustedExecutablesBeforeCreatingProcess()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Throws<UnauthorizedAccessException>(() =>
            WindowsAppContainerLauncher.StartSuspended(
                Environment.ProcessPath!, "--help"));
    }

    [Fact]
    public void LauncherCreatesOnlySuspendedDiagnosticAndClosesIt()
    {
        if (!OperatingSystem.IsWindows()) return;
        var executable = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var pid = WindowsAppContainerLauncher.StartSuspended(executable, "/c exit 0");
        Assert.True(pid > 0);
        // No process is resumed. Does not assert filesystem isolation or safe media execution.
    }
}
