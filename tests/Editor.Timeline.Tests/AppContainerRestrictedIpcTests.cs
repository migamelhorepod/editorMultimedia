using Editor.Security;
using Xunit;

namespace Editor.Timeline.Tests;

public sealed class AppContainerRestrictedIpcTests
{
    [Fact]
    public void RestrictedWorkerHandshake()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", ".."));
        var executable = Path.Combine(root, "TestResults", "sandbox-worker",
            "Editor.Security.Worker.exe");
        Assert.True(File.Exists(executable));
        var pipe = new WindowsWorkerPipe(new AccessGrantRegistry(Guid.NewGuid()));
        Assert.Equal(0, WindowsAppContainerLauncher.RunRestrictedIpcProbe(executable, pipe));
    }
}
