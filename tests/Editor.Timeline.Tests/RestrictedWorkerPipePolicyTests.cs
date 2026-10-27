using System.Security.Principal;
using Editor.Security;
using Xunit;

namespace Editor.Timeline.Tests;

public sealed class RestrictedWorkerPipePolicyTests
{
    [Fact]
    public void PipePolicyGrantsOnlyControllerSystemAndExactAppContainer()
    {
        if (!OperatingSystem.IsWindows()) return;
        var controller = WindowsIdentity.GetCurrent().User!;
        var worker = new SecurityIdentifier(WindowsAppContainerPreflight.DerivePackageSid());
        var sddl = RestrictedWorkerPipePolicy.BuildDacl(controller, worker);
        Assert.Contains(controller.Value, sddl);
        Assert.Contains(worker.Value, sddl);
        Assert.Contains("D:P", sddl);
        Assert.DoesNotContain(";;;WD)", sddl);
        Assert.DoesNotContain(";;;AC)", sddl);
        Assert.Throws<UnauthorizedAccessException>(
            RestrictedWorkerPipePolicy.DemandConfiguredPipeEndpoint);
    }

    [Fact]
    public void PipePolicyRejectsNonAppContainerIdentity()
    {
        if (!OperatingSystem.IsWindows()) return;
        var sid = WindowsIdentity.GetCurrent().User!;
        Assert.Throws<ArgumentException>(() => RestrictedWorkerPipePolicy.BuildDacl(sid, sid));
    }
}
