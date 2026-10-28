using Editor.Security.Worker;
using System.Runtime.InteropServices;
using System.Net.Sockets;
using System.Diagnostics;
using Editor.Security;

// Diagnostic-only worker. File probes never return file contents and must be
// launched by WindowsAppContainerLauncher under a verified AppContainer token.
if (args.Length == 2 && args[0] == "--probe-denied-file")
{
    if (!OperatingSystem.IsWindows() || !IsAppContainerProcess())
        return 70; // A non-sandboxed invocation MUST NOT touch the file.
    if (!Path.IsPathFullyQualified(args[1]))
        return 71;
    try
    {
        using var stream = File.OpenRead(args[1]);
        return 72; // Test fails: OS allowed reading a protected file.
    }
    catch (UnauthorizedAccessException)
    {
        return 0; // Windows enforced the file permission boundary.
    }
    catch (IOException)
    {
        return 73; // Missing/locked files are not proof of access denial.
    }
}

if (args.Length == 2 && args[0] == "--probe-denied-network")
{
    if (!OperatingSystem.IsWindows() || !IsAppContainerProcess())
        return 70;
    if (!int.TryParse(args[1], out int port) || port is < 1 or > 65535)
        return 71;
    using var client = new TcpClient();
    try
    {
        using var networkTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await client.ConnectAsync("127.0.0.1", port, networkTimeout.Token);
        return 72; // Security failure: outbound connection succeeded.
    }
    catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AccessDenied)
    {
        return 0; // Kernel refused network access.
    }
    catch (Exception ex) when (ex is SocketException or OperationCanceledException)
    {
        return 73; // Timeout/refused: NOT proof of sandbox denial.
    }
}

// Fixed external IPv4 canary: no DNS, no user URL, no payload transfer.
// 0 is an explicit socket access denial; 73 means inconclusive timeout/refused.
if (args.Length == 1 && args[0] == "--probe-external-network")
{
    if (!OperatingSystem.IsWindows() || !IsAppContainerProcess()) return 70;
    using var socket = new TcpClient();
    try
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await socket.ConnectAsync("1.1.1.1", 443, deadline.Token);
        return 72; // Unacceptable: external egress succeeded.
    }
    catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AccessDenied)
    {
        return 0;
    }
    catch (Exception ex) when (ex is SocketException or OperationCanceledException)
    {
        return 73; // Not proof of WFP policy.
    }
}

if (args.Length == 2 && args[0] == "--restricted-pipe")
{
    if (!OperatingSystem.IsWindows() || !IsAppContainerProcess()) return 70;
    try
    {
        using var pipeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        var reply = await WindowsWorkerPipe.ConnectRestrictedOnceAsync(
            args[1], WorkerRequest.Heartbeat(), pipeTimeout.Token);
        return reply.Accepted ? 0 : 74; // Diagnostic heartbeat is allowed; media remains blocked.
    }
    catch (Exception ex) when (ex is IOException or InvalidDataException or
        UnauthorizedAccessException or OperationCanceledException or TimeoutException)
    {
        return 73;
    }
}

// MIG-12 research-only path: FFprobe reads a regular file HANDLE received
// as inherited STDIN (fd: default read descriptor 0). No filesystem path is delivered to this worker.
// This is not an enabled production media operation.
if (args.Length == 3 && args[0] == "--probe-fd-metadata")
{
    if (!OperatingSystem.IsWindows() || !IsAppContainerProcess()) return 70;
    if (!Guid.TryParseExact(args[2], "N", out var requestId) ||
        requestId == Guid.Empty) return 71;
    string ffprobe = Path.Combine(AppContext.BaseDirectory, "ffprobe.exe");
    if (!File.Exists(ffprobe)) return 75;
    try
    {
        using var probeDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(250));
        var (exitCode, output) =
            await WindowsInheritedHandleFfprobe.ReadDurationAsync(
                ffprobe, probeDeadline.Token);
        if (exitCode != 0) return 77;
        string? durationLine = output.Split('\n').FirstOrDefault(
            s => s.Trim().StartsWith("duration=", StringComparison.Ordinal));
        if (durationLine is null ||
            !double.TryParse(durationLine.Trim()["duration=".Length..],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var duration) ||
            !double.IsFinite(duration) || duration is <= 0 or > 604800)
            return 79;
        long ticks = checked((long)Math.Round(duration * TimeSpan.TicksPerSecond));
        using var pipeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await WindowsWorkerMetadataPipe.SubmitOnceAsync(args[1],
            new WindowsWorkerMetadataPipe.MetadataReport(
                WorkerRequest.CurrentVersion, requestId, ticks), pipeTimeout.Token);
        return 0;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
        System.ComponentModel.Win32Exception or OperationCanceledException or
        InvalidDataException or OverflowException or TimeoutException)
    {
        return 78;
    }
}

// POC-003.2 proof: decode one authorized inherited seekable file handle
// using real FFmpeg without a staged copy, within the AppContainer Job.
if (args.Length == 3 && args[0] == "--probe-fd-decode")
{
    if (!OperatingSystem.IsWindows() || !IsAppContainerProcess()) return 70;
    if (!Guid.TryParseExact(args[2], "N", out Guid requestId) ||
        requestId == Guid.Empty) return 71;
    string codec = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
    if (!File.Exists(codec)) return 75;
    try
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(250));
        if (await WindowsInheritedHandleFfprobe.DecodeToNullAsync(codec, deadline.Token) != 0)
            return 77;
        using var reportDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await WindowsWorkerMetadataPipe.SubmitOnceAsync(args[1],
            new WindowsWorkerMetadataPipe.MetadataReport(
                WorkerRequest.CurrentVersion, requestId, 1),
            reportDeadline.Token);
        return 0;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
        System.ComponentModel.Win32Exception or OperationCanceledException or
        TimeoutException or InvalidDataException)
    {
        return 78;
    }
}

// Test-only codec subprocess: the launcher verifies AppContainer identity and
// applies a two-member Job before any worker code executes. Only a synthetic
// fixture with a dedicated Windows ACL is permitted by the test harness.
if ((args.Length == 2 && (args[0] == "--probe-ffprobe" || args[0] == "--probe-ffmpeg" ||
    args[0] == "--process-metadata" || args[0] == "--process-decode")) ||
    (args.Length == 4 && (args[0] == "--process-metadata-report" ||
                          args[0] == "--probe-held-decode")))
{
    if (!OperatingSystem.IsWindows() || !IsAppContainerProcess()) return 70;
    if (!Path.IsPathFullyQualified(args[1])) return 71;
    string tool = args[0] is "--probe-ffmpeg" or "--process-decode" or "--probe-held-decode"
        ? "ffmpeg.exe" : "ffprobe.exe";
    string executable = Path.Combine(AppContext.BaseDirectory, tool);
    if (!File.Exists(executable)) return 75;
    var start = new ProcessStartInfo(executable)
    {
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };
    if (tool == "ffmpeg.exe") start.ArgumentList.Add("-nostdin");
    // Test-only forced-kill proof: keep a real decoder alive until its
    // controller authenticates readiness and terminates the Job Object.
    if (args[0] == "--probe-held-decode") start.ArgumentList.Add("-re");
    start.ArgumentList.Add("-v");
    start.ArgumentList.Add("error");
    start.ArgumentList.Add("-i");
    start.ArgumentList.Add(args[1]);
    if (tool == "ffprobe.exe")
    {
        start.ArgumentList.Add("-show_entries");
        start.ArgumentList.Add("format=duration");
        start.ArgumentList.Add("-of");
        start.ArgumentList.Add("default=noprint_wrappers=1");
    }
    else
    {
        start.ArgumentList.Add("-f");
        start.ArgumentList.Add("null");
        start.ArgumentList.Add("-");
    }
    try
    {
        using var process = Process.Start(start);
        if (process is null) return 76;
        if (args[0] == "--probe-held-decode")
        {
            if (!Guid.TryParseExact(args[3], "N", out Guid readyRequestId) ||
                readyRequestId == Guid.Empty)
                return 71;
            await Task.Delay(150);
            if (process.HasExited) return 77;
            using var readyDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            await WindowsWorkerMetadataPipe.SubmitOnceAsync(args[2],
                new WindowsWorkerMetadataPipe.MetadataReport(
                    WorkerRequest.CurrentVersion, readyRequestId, 1), readyDeadline.Token);
            // No success exit: only the controller's Job close may end both
            // this worker and the real FFmpeg subprocess in this test.
            await Task.Delay(Timeout.InfiniteTimeSpan);
            return 78;
        }
        // A genuine decode may exceed a brief diagnostic timeout on HDD.
        // The controller Job enforces an outer 5-minute deadline.
        using var cancel = new CancellationTokenSource(
            TimeSpan.FromSeconds(args[0].StartsWith("--process-", StringComparison.Ordinal) ? 240 : 4));
        var output = process.StandardOutput.ReadToEndAsync(cancel.Token);
        var error = process.StandardError.ReadToEndAsync(cancel.Token);
        await process.WaitForExitAsync(cancel.Token);
        var stdout = await output;
        _ = await error;
        if (process.ExitCode != 0) return 77;
        if (tool == "ffprobe.exe")
        {
            var durationLine = stdout.Split('\n').FirstOrDefault(
                line => line.Trim().StartsWith("duration=", StringComparison.Ordinal));
            if (durationLine is null ||
                !double.TryParse(durationLine.Trim()["duration=".Length..],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double duration) ||
                (!double.IsFinite(duration) || (args[0] is "--process-metadata" or "--process-metadata-report" ? duration is <= 0 or > 604800 : duration is < 0.09 or > 0.11)))
                return 79; // Reject missing or invalid metadata values.
            if (args[0] == "--process-metadata-report")
            {
                if (!Guid.TryParseExact(args[3], "N", out Guid requestId) ||
                    requestId == Guid.Empty ||
                    !double.IsFinite(duration) || duration <= 0 || duration > 604800)
                    return 71;
                long ticks = checked((long)Math.Round(duration * TimeSpan.TicksPerSecond));
                var report = new WindowsWorkerMetadataPipe.MetadataReport(
                    WorkerRequest.CurrentVersion, requestId, ticks);
                using var reportTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                await WindowsWorkerMetadataPipe.SubmitOnceAsync(
                    args[2], report, reportTimeout.Token);
            }
        }
        return 0;
    }
    catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or
        System.ComponentModel.Win32Exception or OperationCanceledException)
    {
        return 78;
    }
}

// Foreign AppContainer proof: only explicit ERROR_ACCESS_DENIED from CreateFile
// on an existing pipe endpoint counts. A timeout or absent pipe is failure.
if (args.Length == 2 && args[0] == "--probe-foreign-pipe")
{
    if (!OperatingSystem.IsWindows() || !IsAppContainerProcess() ||
        !args[1].StartsWith(WindowsWorkerPipe.Prefix, StringComparison.Ordinal))
        return 70;
    IntPtr handle = CreateFileW(@"\\.\pipe\" + args[1], 0xC0000000u, 0,
        IntPtr.Zero, 3, 0, IntPtr.Zero);
    if (handle != new IntPtr(-1))
    {
        CloseHandle(handle);
        return 72; // Incorrectly granted access to a foreign package SID.
    }
    return Marshal.GetLastWin32Error() == 5 ? 0 : 73;
}

// Existing diagnostic IPC; media stays blocked.
if (args.Length != 2 || args[0] != "--pipe")
{
    Console.Error.WriteLine("Unsupported diagnostic operation.");
    return 64;
}
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
try
{
    var reply = await WindowsWorkerPipe.ConnectOnceAsync(
        args[1], WorkerRequest.Heartbeat(), timeout.Token);
    return reply.Accepted ? 0 : 1;
}
catch (Exception ex) when (ex is IOException or InvalidDataException or
    UnauthorizedAccessException or ArgumentException or
    OperationCanceledException or PlatformNotSupportedException)
{
    Console.Error.WriteLine($"Diagnostic IPC failed: {ex.GetType().Name}");
    return 1;
}

static bool IsAppContainerProcess()
{
    if (!OpenProcessToken(GetCurrentProcess(), 0x0008, out var token))
        return false;
    try
    {
        return GetTokenInformation(token, 29, out int value,
            sizeof(int), out int size) && size == sizeof(int) && value == 1;
    }
    finally
    {
        CloseHandle(token);
    }
}

[DllImport("kernel32.dll")]
static extern IntPtr GetCurrentProcess();
[DllImport("advapi32.dll", SetLastError = true)]
static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
[DllImport("advapi32.dll", SetLastError = true)]
static extern bool GetTokenInformation(IntPtr token, int infoClass,
    out int value, int length, out int lengthReturned);
[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
static extern IntPtr CreateFileW(string path, uint access, uint share, IntPtr security,
    uint creation, uint flags, IntPtr templateFile);
[DllImport("kernel32.dll")]
static extern bool CloseHandle(IntPtr handle);
