using Editor.Security;
using System.Net;
using System.Net.Sockets;
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
    public void DiagnosticLauncherRejectsArbitraryShellArguments()
    {
        if (!OperatingSystem.IsWindows()) return;
        var executable = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        Assert.Throws<UnauthorizedAccessException>(() =>
            WindowsAppContainerLauncher.StartSuspended(executable, "/c whoami"));
        Assert.Throws<UnauthorizedAccessException>(() =>
            WindowsAppContainerLauncher.StartSuspended(executable, "/c exit 0 & whoami"));
    }

    [Fact]
    public void FixedDiagnosticActuallyExecutesUnderWindowsAppContainer()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Equal(0, WindowsAppContainerLauncher.RunRestrictedDiagnostic());
        Assert.False(MediaWorkerAdmission.IsMediaProcessingAvailable);
    }

    [Fact]
    public void RealWorkerCannotReadPrivateFileInsideWindowsAppContainer()
    {
        if (!OperatingSystem.IsWindows()) return;
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name
            ?? throw new InvalidOperationException("Missing test configuration.");
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", ".."));
        var worker = Path.Combine(root, "TestResults", "sandbox-worker", "Editor.Security.Worker.exe");
        Assert.True(File.Exists(worker), $"Worker must be built: {worker}");
        var privateFile = Path.Combine(Path.GetTempPath(),
            "editor-poc003-private-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(privateFile, "Synthetic secret: never use private user data.");
        try
        {
            var code = WindowsAppContainerLauncher.RunDeniedFileProbe(worker, privateFile);
            Assert.True(code == 0, $"Worker file probe exit code: {code} (0x{(uint)code:X8})");
        }
        finally
        {
            File.Delete(privateFile);
        }
    }

    [Fact]
    public void RealWorkerNetworkAttemptMustBeDeniedByWindows()
    {
        if (!OperatingSystem.IsWindows()) return;
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name
            ?? throw new InvalidOperationException("Missing configuration.");
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", ".."));
        var worker = Path.Combine(root, "TestResults", "sandbox-worker", "Editor.Security.Worker.exe");
        Assert.True(File.Exists(worker));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var code = WindowsAppContainerLauncher.RunDeniedNetworkProbe(worker, port);
        Assert.True(code == 0, $"Worker network probe exit code: {code} (0x{(uint)code:X8})");
    }

    [Fact]
    public void LauncherVerifiesKernelAppContainerTokenBeforeReturning()
    {
        if (!OperatingSystem.IsWindows()) return;
        var executable = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var pid = WindowsAppContainerLauncher.StartSuspended(executable, "/c exit 0");
        Assert.True(pid > 0);
        // No process is resumed. Does not assert filesystem isolation or safe media execution.
    }
}
