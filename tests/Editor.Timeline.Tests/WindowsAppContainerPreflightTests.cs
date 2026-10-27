using Editor.Security;
using Xunit;

namespace Editor.Timeline.Tests;

public sealed class WindowsAppContainerPreflightTests
{
    [Fact]
    public void AppContainerSidCanBeDerivedOnWindowsWithoutStartingWorker()
    {
        if (!OperatingSystem.IsWindows()) return;
        var sid = WindowsAppContainerPreflight.DerivePackageSid();
        Assert.StartsWith("S-1-15-2-", sid);
        Assert.Equal(sid, WindowsAppContainerPreflight.DerivePackageSid());
    }

    [Fact]
    public void SandboxLaunchIsProhibitedUntilVerified()
    {
        var failure = Assert.Throws<UnauthorizedAccessException>(
            WindowsAppContainerPreflight.DemandSandboxedLaunch);
        Assert.Contains("no unrestricted fallback", failure.Message);
    }
}
