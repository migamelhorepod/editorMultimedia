using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace Editor.Security;

/// <summary>
/// Experimental native AppContainer launch. This is an OS process identity
/// boundary; media remains prohibited until resource, IPC and adversarial
/// checks have been proven on Windows.
/// </summary>
public static class WindowsAppContainerLauncher
{
    // xUnit runs sandbox tests across classes concurrently; Windows profile
    // provisioning is serialized, while worker execution stays parallel.
    private static readonly object ProfileCreationLock = new();
    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const uint CreateSuspended = 0x00000004;
    private static readonly nuint SecurityCapabilitiesAttribute = 0x00020009;
    private static readonly nuint InheritedHandleListAttribute = 0x00020002;
    private const uint DuplicateSameAccess = 0x00000002;

    public static int StartSuspended(string executable, string arguments) =>
        LaunchDiagnostic(executable, arguments, false);

    /// <summary>
    /// Execute only the fixed inert Windows diagnostic INSIDE AppContainer.
    /// Returns process exit status, not proof of media filesystem isolation.
    /// </summary>
    public static int RunRestrictedDiagnostic()
    {
        var executable = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        return LaunchDiagnostic(executable, "/c exit 0", true);
    }

    /// <summary>
    /// Exercise a real .NET worker inside AppContainer against an existing
    /// protected file. Zero means the kernel denied access, not a file error.
    /// Never pass a media file; only private synthetic test fixtures.
    /// </summary>
    public static int RunDeniedFileProbe(string workerExecutable, string protectedFile)
    {
        if (!Path.IsPathFullyQualified(protectedFile) || !File.Exists(protectedFile) ||
            protectedFile.IndexOf((char)34) >= 0 || protectedFile.IndexOf((char)10) >= 0 ||
            protectedFile.IndexOf((char)13) >= 0)
            throw new ArgumentException("Existing absolute test fixture required.", nameof(protectedFile));
        if (!string.Equals(Path.GetFileName(workerExecutable), "Editor.Security.Worker.exe",
            StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Only the dedicated worker executable is permitted.");
        return LaunchDiagnostic(workerExecutable,
            "--probe-denied-file " + ((char)34) + protectedFile + ((char)34), true);
    }

    public static int RunDeniedNetworkProbe(string workerExecutable, int port)
    {
        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port));
        if (!string.Equals(Path.GetFileName(workerExecutable), "Editor.Security.Worker.exe",
            StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Only the real diagnostic worker is permitted.");
        return LaunchDiagnostic(workerExecutable, "--probe-denied-network " + port, true);
    }

    public static int RunExternalNetworkProbe(string workerExecutable)
    {
        if (!string.Equals(Path.GetFileName(workerExecutable), "Editor.Security.Worker.exe",
            StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Only the diagnostic worker is permitted.");
        return LaunchDiagnostic(workerExecutable, "--probe-external-network", true);
    }

    public static int RunRestrictedIpcProbe(string workerExecutable, WindowsWorkerPipe pipe)
    {
        ArgumentNullException.ThrowIfNull(pipe);
        if (!string.Equals(Path.GetFileName(workerExecutable), "Editor.Security.Worker.exe", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Unexpected IPC diagnostic worker.");
        return LaunchDiagnostic(workerExecutable, "--restricted-pipe " + pipe.PipeName, true,
            pid => pipe.ServeRestrictedOnceAsync(pid,
                new CancellationTokenSource(TimeSpan.FromSeconds(6)).Token));
    }

    public static (int exitCode, int processId) RunExternalNetworkProbeObserved(string workerExecutable)
    {
        int pid = 0;
        if (!string.Equals(Path.GetFileName(workerExecutable), "Editor.Security.Worker.exe",
            StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Unexpected network diagnostic executable.");
        int exitCode = LaunchDiagnostic(workerExecutable, "--probe-external-network", true,
            null, verifiedPid => pid = verifiedPid);
        if (pid <= 0) throw new InvalidOperationException("No verified AppContainer process was created.");
        return (exitCode, pid);
    }

    /// <summary>
    /// Launch real ffprobe inside the verified AppContainer for one registered
    /// library resource and InspectMetadata grant. The kernel must separately
    /// permit read access to that one file; this API never changes file ACLs
    /// and never runs ffprobe outside the sandbox on access failure.
    /// Exit 0 proves a bounded metadata probe completed, not authorization
    /// for unrestricted media processing.
    /// </summary>
    public static int RunAuthorizedMetadataProcessing(
        string workerExecutable, AuthorizedMediaCatalog catalog, WorkerRequest request)
    {
        if (request.Operation != MediaAccessOperation.InspectMetadata)
            throw new UnauthorizedAccessException("Metadata processing requires InspectMetadata grant.");
        return RunWithInheritedReadHandle(workerExecutable, catalog, request,
            input => LaunchDiagnostic(workerExecutable,
                "--process-metadata-handle " + input.ToInt64(), true,
                inheritedInputHandle: input));
    }

    /// <summary>
    /// Decode real registered media with FFmpeg inside the AppContainer into a
    /// null sink (no exported media). Requires a separate ReadChunk task grant.
    /// A private expendable copy receives only AppContainer read permission;
    /// the original file ACL is never modified.
    /// </summary>
    /// <summary>
    /// Return actual, bounded FFprobe duration over a kernel-PID-authenticated
    /// AppContainer-only named pipe. No controller-side media decoding occurs.
    /// </summary>
    public static long ReadAuthorizedMetadataTicks(
        string workerExecutable, AuthorizedMediaCatalog catalog, WorkerRequest request)
    {
        if (request.Operation != MediaAccessOperation.InspectMetadata)
            throw new UnauthorizedAccessException("Metadata requires InspectMetadata authorization.");
        var pipe = new WindowsWorkerMetadataPipe();
        long ticks = 0;
        int exit = RunWithInheritedReadHandle(workerExecutable, catalog, request,
            input => LaunchDiagnostic(workerExecutable,
                "--process-metadata-handle " + input.ToInt64() + " " +
                    pipe.Name + " " + request.RequestId.ToString("N"), true,
                async pid =>
                {
                    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(250));
                    ticks = await pipe.ReceiveOnceAsync(pid, request.RequestId, deadline.Token);
                    return new WorkerResponse(WorkerRequest.CurrentVersion,
                        request.RequestId, AccessDecision.Allowed);
                }, inheritedInputHandle: input));
        if (exit != 0 || ticks <= 0)
            throw new InvalidDataException("Isolated worker did not return valid metadata.");
        return ticks;
    }

    public static int RunAuthorizedDecodeProcessing(
        string workerExecutable, AuthorizedMediaCatalog catalog, WorkerRequest request)
    {
        if (request.Operation != MediaAccessOperation.ReadChunk)
            throw new UnauthorizedAccessException("Real decoding requires a ReadChunk grant.");
        return RunWithInheritedReadHandle(workerExecutable, catalog, request,
            input => LaunchDiagnostic(workerExecutable,
                "--process-decode-handle " + input.ToInt64(), true,
                inheritedInputHandle: input));
    }

    /// <summary>
    /// Security regression only: authenticate a live decoder and close its
    /// Job Object; the direct source HANDLE must become unusable afterwards.
    /// </summary>
    public static int RunAuthorizedInterruptedDecodeProbe(
        string workerExecutable, AuthorizedMediaCatalog catalog, WorkerRequest request)
    {
        if (request.Operation != MediaAccessOperation.ReadChunk)
            throw new UnauthorizedAccessException("An interrupted decoder requires ReadChunk authorization.");
        var pipe = new WindowsWorkerMetadataPipe();
        return RunWithInheritedReadHandle(workerExecutable, catalog, request,
            input => LaunchDiagnostic(workerExecutable,
                "--probe-held-decode-handle " + input.ToInt64() + " " +
                    pipe.Name + " " + request.RequestId.ToString("N"), true,
                async pid =>
                {
                    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                    _ = await pipe.ReceiveOnceAsync(pid, request.RequestId, deadline.Token);
                    return new WorkerResponse(WorkerRequest.CurrentVersion,
                        request.RequestId, AccessDecision.Allowed);
                }, terminateAfterHandshake: true, inheritedInputHandle: input));
    }

    /// <summary>
    /// No staging copy, original ACL change, or filesystem path crosses the
    /// AppContainer boundary. Windows inherits exactly one read-only handle
    /// via PROC_THREAD_ATTRIBUTE_HANDLE_LIST. FFmpeg receives a SEEKABLE
    /// regular-file handle and never opens the original pathname itself.
    /// </summary>
    private static int RunWithInheritedReadHandle(string workerExecutable,
        AuthorizedMediaCatalog catalog, WorkerRequest request, Func<IntPtr, int> launch)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (!string.Equals(Path.GetFileName(workerExecutable),
                "Editor.Security.Worker.exe", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Only the isolated media worker is permitted.");
        string source = catalog.Resolve(request);
        // Open the object itself, never follow a last-component reparse point.
        // GENERIC_READ, FILE_SHARE_READ, OPEN_EXISTING, OPEN_REPARSE_POINT.
        using SafeFileHandle opened = CreateReadOnlyMediaHandle(source, 0x80000000u,
            0x00000001u, IntPtr.Zero, 3u, 0x00200000u | 0x10000000u,
            IntPtr.Zero);
        if (opened.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Cannot open only the authorized original media.");
        if (!GetFileInformationByHandle(opened, out var sourceInfo) ||
            (sourceInfo.Attributes & (uint)FileAttributes.ReparsePoint) != 0)
            throw new UnauthorizedAccessException("A redirected media file handle is not permitted.");
        long length = RandomAccess.GetLength(opened);
        if (length <= 0 || length > 10L * 1024 * 1024 * 1024)
            throw new IOException("Authorized media exceeds the bounded input size.");
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0 ||
            !string.Equals(catalog.Resolve(request), source, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Authorized media changed during handle acquisition.");

        if (!DuplicateHandle(GetCurrentProcess(), opened.DangerousGetHandle(),
                GetCurrentProcess(), out IntPtr inherited, 0, true, DuplicateSameAccess))
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Could not prepare the single read-only inherited media handle.");
        try
        {
            return launch(inherited);
        }
        finally
        {
            CloseHandle(inherited);
        }
    }

    public static int RunAuthorizedCodecIsolationProbe(
        string workerExecutable, AuthorizedMediaCatalog catalog,
        WorkerRequest request, bool useFfmpeg)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        string authorizedPath = catalog.Resolve(request);
        return RunCodecIsolationProbe(workerExecutable, authorizedPath, useFfmpeg);
    }

    private static int RunCodecIsolationProbe(string workerExecutable, string syntheticInput, bool useFfmpeg)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (!string.Equals(Path.GetFileName(workerExecutable), "Editor.Security.Worker.exe", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Unexpected codec diagnostic worker.");
        if (!Path.IsPathFullyQualified(syntheticInput) || !File.Exists(syntheticInput) ||
            syntheticInput.IndexOf((char)34) >= 0 || syntheticInput.IndexOf((char)10) >= 0)
            throw new ArgumentException("Absolute existing synthetic file required.", nameof(syntheticInput));
        string command = (useFfmpeg ? "--probe-ffmpeg " : "--probe-ffprobe ") +
            ((char)34) + syntheticInput + ((char)34);
        return LaunchDiagnostic(workerExecutable, command, true);
    }

    public static int RunForeignAppContainerPipeProbe(string workerExecutable, string pipeName)
    {
        if (!string.Equals(Path.GetFileName(workerExecutable), "Editor.Security.Worker.exe", StringComparison.OrdinalIgnoreCase) ||
            pipeName.Length != WindowsWorkerPipe.Prefix.Length + 32 ||
            !pipeName.StartsWith(WindowsWorkerPipe.Prefix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(pipeName.AsSpan(WindowsWorkerPipe.Prefix.Length), "N", out _))
            throw new UnauthorizedAccessException("Invalid foreign AppContainer pipe probe.");
        return LaunchDiagnostic(workerExecutable, "--probe-foreign-pipe " + pipeName,
            true, profileName: WindowsAppContainerPreflight.ProfileName + ".ForeignSecurityTest");
    }

    private static int LaunchDiagnostic(string executable, string arguments, bool run,
        Func<int, Task<WorkerResponse>>? authenticatedServer = null,
        Action<int>? processVerified = null, string? profileName = null,
        bool terminateAfterHandshake = false, IntPtr inheritedInputHandle = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException();
        if (string.IsNullOrWhiteSpace(executable) || !Path.IsPathFullyQualified(executable)
            || !File.Exists(executable))
            throw new ArgumentException("An existing absolute executable path is required.", nameof(executable));
        // Never accept a caller-supplied path to media or unspecified executable.
        var isInertCommand = string.Equals(Path.GetFullPath(executable),
            Path.GetFullPath(Path.Combine(Environment.SystemDirectory, "cmd.exe")),
            StringComparison.OrdinalIgnoreCase) &&
            string.Equals(arguments, "/c exit 0", StringComparison.Ordinal);
        var isWorkerProbe = string.Equals(Path.GetFileName(executable),
            "Editor.Security.Worker.exe", StringComparison.OrdinalIgnoreCase) &&
            arguments.StartsWith("--probe-denied-file " + ((char)34), StringComparison.Ordinal) &&
            arguments.EndsWith(((char)34).ToString(), StringComparison.Ordinal) && run;
        var isNetworkProbe = string.Equals(Path.GetFileName(executable),
            "Editor.Security.Worker.exe", StringComparison.OrdinalIgnoreCase) &&
            arguments.StartsWith("--probe-denied-network ", StringComparison.Ordinal) && run;
        var isExternalProbe = string.Equals(Path.GetFileName(executable),
            "Editor.Security.Worker.exe", StringComparison.OrdinalIgnoreCase) &&
            arguments == "--probe-external-network" && run;
        var isIpcProbe = string.Equals(Path.GetFileName(executable),
            "Editor.Security.Worker.exe", StringComparison.OrdinalIgnoreCase) &&
            arguments.StartsWith("--restricted-pipe editor-poc003-", StringComparison.Ordinal) &&
            authenticatedServer is not null && run;
        var isCodecProbe = string.Equals(Path.GetFileName(executable),
            "Editor.Security.Worker.exe", StringComparison.OrdinalIgnoreCase) &&
            (arguments.StartsWith("--probe-ffprobe " + ((char)34), StringComparison.Ordinal) ||
             arguments.StartsWith("--probe-ffmpeg " + ((char)34), StringComparison.Ordinal) ||
             arguments.StartsWith("--process-metadata " + ((char)34), StringComparison.Ordinal) ||
             arguments.StartsWith("--process-decode " + ((char)34), StringComparison.Ordinal)) &&
            arguments.EndsWith(((char)34).ToString(), StringComparison.Ordinal) && run;
        var isForeignProbe = string.Equals(Path.GetFileName(executable),
            "Editor.Security.Worker.exe", StringComparison.OrdinalIgnoreCase) &&
            arguments.StartsWith("--probe-foreign-pipe editor-poc003-", StringComparison.Ordinal) &&
            profileName == WindowsAppContainerPreflight.ProfileName + ".ForeignSecurityTest" && run;
        var isMetadataReportProbe = string.Equals(Path.GetFileName(executable),
            "Editor.Security.Worker.exe", StringComparison.OrdinalIgnoreCase) &&
            arguments.StartsWith("--process-metadata-report \"", StringComparison.Ordinal) &&
            authenticatedServer is not null && run;
        var isHeldDecoderProbe = string.Equals(Path.GetFileName(executable),
            "Editor.Security.Worker.exe", StringComparison.OrdinalIgnoreCase) &&
            arguments.StartsWith("--probe-held-decode \"", StringComparison.Ordinal) &&
            authenticatedServer is not null && terminateAfterHandshake && run;
        var isHandleCodec = inheritedInputHandle != IntPtr.Zero && run &&
            string.Equals(Path.GetFileName(executable), "Editor.Security.Worker.exe",
                StringComparison.OrdinalIgnoreCase) &&
            (arguments.StartsWith("--process-metadata-handle ", StringComparison.Ordinal) ||
             arguments.StartsWith("--process-decode-handle ", StringComparison.Ordinal) ||
             arguments.StartsWith("--probe-held-decode-handle ", StringComparison.Ordinal));
        if (!isInertCommand && !isWorkerProbe && !isNetworkProbe && !isExternalProbe && !isIpcProbe && !isCodecProbe && !isForeignProbe && !isMetadataReportProbe && !isHeldDecoderProbe && !isHandleCodec)
            throw new UnauthorizedAccessException("Unsupported AppContainer executable or operation.");

        // No arbitrary command line may reach a diagnostic shell.
        // This process remains suspended and is terminated before executing.
        if (!run && !isInertCommand)
            throw new UnauthorizedAccessException("Suspended diagnostics only support the inert command.");

        string selectedProfile = profileName ?? WindowsAppContainerPreflight.ProfileName;
        IntPtr sid = IntPtr.Zero, attributes = IntPtr.Zero, capabilitiesPtr = IntPtr.Zero;
        IntPtr inheritedList = IntPtr.Zero;
        PROCESS_INFORMATION process = default;
        WindowsWorkerJob? resourceJob = null;
        bool initialized = false;
        try
        {
            int hr;
            lock (ProfileCreationLock)
            {
                hr = CreateAppContainerProfile(
                    selectedProfile,
                    "Editor Multimedia Diagnostic", "POC-003.1 test-only profile",
                    IntPtr.Zero, 0, out sid);
                // ERROR_ALREADY_EXISTS is expected on repeated CI runs.
                if (hr == unchecked((int)0x800700B7))
                    hr = DeriveAppContainerSidFromAppContainerName(
                        selectedProfile, out sid);
            }
            if (hr < 0 || sid == IntPtr.Zero)
                throw new COMException(
                    $"Windows AppContainer profile provisioning failed for {selectedProfile}: HRESULT 0x{(uint)hr:X8}. No sandbox fallback permitted.",
                    hr < 0 ? hr : unchecked((int)0x80004005));

            nuint bytes = 0;
            int attributeCount = inheritedInputHandle != IntPtr.Zero ? 2 : 1;
            InitializeProcThreadAttributeList(IntPtr.Zero, attributeCount, 0, ref bytes);
            if (bytes == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            attributes = Marshal.AllocHGlobal(checked((nint)bytes));
            if (!InitializeProcThreadAttributeList(attributes, attributeCount, 0, ref bytes))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            initialized = true;

            var caps = new SECURITY_CAPABILITIES { AppContainerSid = sid };
            capabilitiesPtr = Marshal.AllocHGlobal(Marshal.SizeOf<SECURITY_CAPABILITIES>());
            Marshal.StructureToPtr(caps, capabilitiesPtr, false);
            if (!UpdateProcThreadAttribute(attributes, 0, SecurityCapabilitiesAttribute,
                capabilitiesPtr, (nuint)Marshal.SizeOf<SECURITY_CAPABILITIES>(),
                IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            if (inheritedInputHandle != IntPtr.Zero)
            {
                inheritedList = Marshal.AllocHGlobal(IntPtr.Size);
                Marshal.WriteIntPtr(inheritedList, inheritedInputHandle);
                if (!UpdateProcThreadAttribute(attributes, 0, InheritedHandleListAttribute,
                        inheritedList, (nuint)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                    throw new Win32Exception(Marshal.GetLastWin32Error(),
                        "Windows refused the exact inherited media handle list.");
            }

            var startup = new STARTUPINFOEX();
            startup.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();
            startup.lpAttributeList = attributes;
            string commandLine = "\"" + executable + "\" " + arguments;
            if (!CreateProcessW(executable, commandLine, IntPtr.Zero, IntPtr.Zero,
                inheritedInputHandle != IntPtr.Zero, ExtendedStartupInfoPresent | CreateSuspended,
                IntPtr.Zero, Environment.SystemDirectory, ref startup, out process))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Windows refused the AppContainer diagnostic process.");
            // Assign hard resource limits before any code could run.
            resourceJob = WindowsWorkerJob.AttachProcessHandle(process.hProcess,
                256 * 1024 * 1024, (isCodecProbe || isMetadataReportProbe || isHeldDecoderProbe || isHandleCodec) ? 2u : 1u);
            // Verify the real process token; requesting a sandbox is not proof.
            if (!OpenProcessToken(process.hProcess, 0x0008, out IntPtr token))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Cannot inspect AppContainer token.");
            try
            {
                if (!GetTokenInformation(token, 29, out int isAppContainer,
                    sizeof(int), out int returned) || returned != sizeof(int) ||
                    isAppContainer != 1)
                    throw new UnauthorizedAccessException(
                        "Process lacks a Windows AppContainer token.");
                // TokenCapabilities = 30, first DWORD is GroupCount in TOKEN_GROUPS.
                // Require zero capabilities; merely setting CapabilityCount=0
                // on the creation request is insufficient evidence.
                IntPtr capabilityBuffer = Marshal.AllocHGlobal(4096);
                try
                {
                    if (!GetTokenInformationBuffer(token, 30, capabilityBuffer,
                        4096, out int capabilityBytes) || capabilityBytes < sizeof(int))
                        throw new Win32Exception(Marshal.GetLastWin32Error(),
                            "Cannot inspect effective AppContainer capabilities.");
                    int count = Marshal.ReadInt32(capabilityBuffer);
                    int offset = IntPtr.Size == 8 ? 8 : 4;
                    int entryBytes = IntPtr.Size == 8 ? 16 : 8;
                    if (count < 0 || count > (capabilityBytes - offset) / entryBytes)
                        throw new InvalidDataException("Malformed Windows token capability list.");
                    for (int i = 0; i < count; i++)
                    {
                        var capabilitySid = Marshal.ReadIntPtr(capabilityBuffer, offset + i * entryBytes);
                        if (capabilitySid == IntPtr.Zero)
                            throw new InvalidDataException("Null capability SID.");
                        var value = new SecurityIdentifier(capabilitySid).Value;
                        if (value is "S-1-15-3-1" or "S-1-15-3-2" or "S-1-15-3-3")
                            throw new UnauthorizedAccessException(
                                "Network capability present in restricted AppContainer token.");
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(capabilityBuffer);
                }
            }
            finally
            {
                CloseHandle(token);
            }
            processVerified?.Invoke(checked((int)process.dwProcessId));
            if (!run) return checked((int)process.dwProcessId);
            Task<WorkerResponse>? serverTask = authenticatedServer?.Invoke(checked((int)process.dwProcessId));
            if (ResumeThread(process.hThread) == uint.MaxValue)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot resume restricted process.");
            // Keep the 5s deadline for diagnostic probes. Real FFmpeg decode on
            // a conventional HDD needs a larger but still bounded deadline.
            // Timeout remains fail-closed: the Job is killed in finally.
            bool realMediaCommand = arguments.StartsWith("--process-", StringComparison.Ordinal);
            // A worker can fail before opening the controller-owned pipe (for
            // example because a packaged runtime dependency is inaccessible).
            // Do not hide that failure behind a 250-second pipe timeout.
            uint maxWaitMs = realMediaCommand ? 300000u : 5000u;
            uint elapsedMs = 0;
            uint wait;
            do
            {
                wait = WaitForSingleObject(process.hProcess, 100u);
                if (wait == 0u) break;
                if (wait != 258u) // WAIT_TIMEOUT
                    throw new Win32Exception(Marshal.GetLastWin32Error(),
                        "Failed while waiting for the restricted worker process.");
                elapsedMs += 100u;
                if (serverTask is { IsFaulted: true } or { IsCanceled: true })
                    serverTask.GetAwaiter().GetResult();
                if (terminateAfterHandshake && serverTask is { IsCompletedSuccessfully: true })
                {
                    // The authenticated worker reported that real FFmpeg is
                    // running. Closing the Job must kill BOTH OS processes.
                    _ = serverTask.GetAwaiter().GetResult();
                    resourceJob.Dispose();
                    resourceJob = null;
                    if (WaitForSingleObject(process.hProcess, 5000u) != 0u)
                        throw new TimeoutException("Forced AppContainer worker termination failed.");
                    return 0; // Explicit successful kill proof, not codec success.
                }
            } while (elapsedMs < maxWaitMs);
            if (wait != 0u)
                throw new TimeoutException("Restricted worker did not exit within its bounded deadline.");
            if (!GetExitCodeProcess(process.hProcess, out uint exitCode))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (serverTask is not null && !serverTask.IsCompleted && exitCode != 0u)
                throw new InvalidOperationException(
                    $"AppContainer worker exited with status {ConvertNativeExitCode(exitCode)} before completing authenticated IPC.");
            if (serverTask is not null)
            {
                var response = serverTask.GetAwaiter().GetResult();
                if (MediaWorkerAdmission.IsMediaProcessingAvailable)
                    throw new UnauthorizedAccessException("Media processing must remain unavailable.");
            }
            return ConvertNativeExitCode(exitCode);
        }
        finally
        {
            resourceJob?.Dispose();
            if (process.hProcess != IntPtr.Zero)
            {
                TerminateProcess(process.hProcess, 1);
                CloseHandle(process.hProcess);
            }
            if (process.hThread != IntPtr.Zero) CloseHandle(process.hThread);
            if (initialized) DeleteProcThreadAttributeList(attributes);
            if (attributes != IntPtr.Zero) Marshal.FreeHGlobal(attributes);
            if (capabilitiesPtr != IntPtr.Zero) Marshal.FreeHGlobal(capabilitiesPtr);
            if (inheritedList != IntPtr.Zero) Marshal.FreeHGlobal(inheritedList);
            if (sid != IntPtr.Zero) FreeSid(sid);
        }
    }

    private static int ConvertNativeExitCode(uint nativeCode) => unchecked((int)nativeCode);

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_CAPABILITIES
    {
        public IntPtr AppContainerSid;
        public IntPtr Capabilities;
        public uint CapabilityCount;
        public uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars;
        public int dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFOEX
    {
        public STARTUPINFO StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public uint dwProcessId, dwThreadId;
    }

    [DllImport("userenv.dll", CharSet = CharSet.Unicode)]
    private static extern int CreateAppContainerProfile(string name, string displayName,
        string description, IntPtr capabilities, uint capabilityCount, out IntPtr sid);
    [DllImport("userenv.dll", CharSet = CharSet.Unicode)]
    private static extern int DeriveAppContainerSidFromAppContainerName(string name, out IntPtr sid);
    [DllImport("advapi32.dll")]
    private static extern IntPtr FreeSid(IntPtr sid);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref nuint size);
    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags,
        nuint attribute, IntPtr value, nuint size, IntPtr previous, IntPtr returnSize);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcessW(string application, string commandLine,
        IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles,
        uint flags, IntPtr environment, string workingDirectory,
        ref STARTUPINFOEX startup, out PROCESS_INFORMATION information);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr token, int informationClass,
        out int information, int length, out int returnLength);
    [DllImport("advapi32.dll", SetLastError = true, EntryPoint = "GetTokenInformation")]
    private static extern bool GetTokenInformationBuffer(IntPtr token, int informationClass,
        IntPtr information, int length, out int returnLength);
    [StructLayout(LayoutKind.Sequential)]
    private struct BY_HANDLE_FILE_INFORMATION
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateReadOnlyMediaHandle(
        string name, uint access, uint share, IntPtr security,
        uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle handle, out BY_HANDLE_FILE_INFORMATION fileInformation);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DuplicateHandle(IntPtr sourceProcess, IntPtr sourceHandle,
        IntPtr targetProcess, out IntPtr targetHandle, uint desiredAccess,
        bool inheritHandle, uint options);
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);
    [DllImport("kernel32.dll")]
    private static extern bool TerminateProcess(IntPtr process, uint exitCode);
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
