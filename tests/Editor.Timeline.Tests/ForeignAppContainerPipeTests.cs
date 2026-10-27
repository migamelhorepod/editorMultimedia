using System.Security.Principal;
using Editor.Security;
using Xunit;

namespace Editor.Timeline.Tests;

public sealed class ForeignAppContainerPipeTests
{
    [Fact]
    public void ForeignPackageSidIsDeniedByWindowsNotByTimeout()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", ".."));
        string worker = Path.Combine(root, "TestResults", "sandbox-worker",
            "Editor.Security.Worker.exe");
        var controller = WindowsIdentity.GetCurrent().User!;
        var approved = new SecurityIdentifier(WindowsAppContainerPreflight.DerivePackageSid());
        string name = WindowsWorkerPipe.Prefix + Guid.NewGuid().ToString("N");
        using var pipe = RestrictedWorkerPipePolicy.CreateEndpoint(name, controller, approved);
        int result = WindowsAppContainerLauncher.RunForeignAppContainerPipeProbe(worker, name);
        Assert.True(result == 0,
            $"Foreign AppContainer must see native ERROR_ACCESS_DENIED; returned {result}.");
    }
}
