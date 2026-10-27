using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Editor.Security;

/// <summary>
/// Defense-in-depth Windows resource limits for a diagnostic child process.
/// A Job Object is NOT a security boundary: it grants no filesystem or network
/// isolation. Never use this class alone to authorize untrusted media decoding.
/// </summary>
public sealed class WindowsWorkerJob : IDisposable
{
    private readonly SafeJobHandle handle;
    private bool disposed;

    private WindowsWorkerJob(SafeJobHandle handle) => this.handle = handle;

    public static WindowsWorkerJob Attach(Process child, nuint memoryLimitBytes = 256 * 1024 * 1024)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Job Objects require Windows.");
        if (memoryLimitBytes < 32 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(memoryLimitBytes));

        return AttachProcessHandle(child.Handle, memoryLimitBytes);
    }

    /// <summary>
    /// Attach a process while its primary thread is still suspended.
    /// The caller must guarantee it has not been resumed and must terminate
    /// it on failure. This does not itself provide a sandbox.
    /// </summary>
    internal static WindowsWorkerJob AttachProcessHandle(IntPtr processHandle,
        nuint memoryLimitBytes = 256 * 1024 * 1024, uint processLimit = 1)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException();
        if (processHandle == IntPtr.Zero || processHandle == new IntPtr(-1))
            throw new ArgumentException("Invalid process handle.", nameof(processHandle));
        if (memoryLimitBytes < 32 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(memoryLimitBytes));
        if (processLimit is < 1 or > 2)
            throw new ArgumentOutOfRangeException(nameof(processLimit));
        SafeJobHandle job = CreateJobObjectW(IntPtr.Zero, null);
        if (job.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            job.Dispose();
            throw new Win32Exception(error, "Cannot create worker Job Object.");
        }

        try
        {
            var limits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                {
                    LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE |
                                 JOB_OBJECT_LIMIT_ACTIVE_PROCESS |
                                 JOB_OBJECT_LIMIT_PROCESS_MEMORY,
                    ActiveProcessLimit = processLimit
                },
                ProcessMemoryLimit = memoryLimitBytes
            };
            int length = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
            IntPtr buffer = Marshal.AllocHGlobal(length);
            try
            {
                Marshal.StructureToPtr(limits, buffer, false);
                if (!SetInformationJobObject(job, 9, buffer, (uint)length))
                    throw new Win32Exception(Marshal.GetLastWin32Error(),
                        "Cannot configure worker Job Object.");
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            if (!AssignProcessToJobObject(job, processHandle))
                throw new InvalidOperationException(
                    "Worker exited or could not be assigned to a restricted job.");
            return new WindowsWorkerJob(job);
        }
        catch
        {
            job.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        handle.Dispose(); // KILL_ON_JOB_CLOSE terminates still-running job members.
    }

    private const uint JOB_OBJECT_LIMIT_ACTIVE_PROCESS = 0x00000008;
    private const uint JOB_OBJECT_LIMIT_PROCESS_MEMORY = 0x00000100;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    private sealed class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeJobHandle() : base(true) { }
        protected override bool ReleaseHandle() => CloseHandle(handle);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeJobHandle CreateJobObjectW(IntPtr attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(SafeJobHandle job,
        int infoClass, IntPtr info, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeJobHandle job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
