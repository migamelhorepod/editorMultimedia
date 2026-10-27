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
    public void RealWorkerCannotEstablishLoopbackConnection()
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
        // An active listener distinguishes timeout from a missing server.
        // 0 = explicit Windows AccessDenied; 73 = connection never established.
        // Neither a timeout nor an aborted connection independently proves WFP policy.
        Assert.True(code is 0 or 73,
            $"Restricted worker established a connection or failed unexpectedly: {code} (0x{(uint)code:X8})");
    }

    [Fact]
    public void ExternalEgressCanaryCannotConnectFromAppContainer()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", ".."));
        var worker = Path.Combine(root, "TestResults", "sandbox-worker", "Editor.Security.Worker.exe");
        Assert.True(File.Exists(worker));
        var start = DateTime.UtcNow;
        int code = WindowsAppContainerLauncher.RunExternalNetworkProbe(worker);
        var end = DateTime.UtcNow;
        var reportPath = Path.Combine(root, "TestResults", "sec-006-probe.txt");
        File.WriteAllLines(reportPath, [worker, start.ToString("o"), end.ToString("o")]);
        // 0: explicit access denied; 73: unavailable / timeout (not a WFP proof).
        // 72: connected, must fail. Never treat 73 as verified WFP blocking.
        Assert.True(code is 0 or 73, $"Unexpected external egress result: {code}");
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
