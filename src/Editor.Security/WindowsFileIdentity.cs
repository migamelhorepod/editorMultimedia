using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Editor.Security;

/// <summary>
/// The NTFS filesystem identity of a controller-approved regular media file.
/// A path alone is insufficient: another file can replace the same name after
/// registration, including while the AppContainer worker is starting.
/// </summary>
internal readonly record struct WindowsFileIdentity(uint VolumeSerial, ulong FileIndex)
{
    internal static WindowsFileIdentity FromPath(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, 4096, FileOptions.RandomAccess);
        return FromHandle(stream.SafeFileHandle);
    }

    internal static WindowsFileIdentity FromHandle(SafeFileHandle handle)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (handle.IsInvalid || handle.IsClosed ||
            !GetFileInformationByHandle(handle, out var info))
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Cannot verify physical media identity.");
        return new WindowsFileIdentity(info.VolumeSerialNumber,
            ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFileTime
    {
        public uint LowDateTime, HighDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public NativeFileTime CreationTime, LastAccessTime, LastWriteTime;
        public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks;
        public uint FileIndexHigh, FileIndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file,
        out ByHandleFileInformation information);
}
