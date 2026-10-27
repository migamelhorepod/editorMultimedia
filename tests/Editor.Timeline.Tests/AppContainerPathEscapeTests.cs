using Editor.Security;
using Xunit;

namespace Editor.Timeline.Tests;

public sealed class AppContainerPathEscapeTests
{
    [Fact]
    public void DirectoryReparsePointCannotEscapeToOtherLibrary()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", ".."));
        var worker = Path.Combine(root, "TestResults", "sandbox-worker",
            "Editor.Security.Worker.exe");
        string main = Path.Combine(Path.GetTempPath(), "poc003-main-" + Guid.NewGuid().ToString("N"));
        string inactive = Path.Combine(Path.GetTempPath(), "poc003-inactive-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(main);
        Directory.CreateDirectory(inactive);
        string privateFile = Path.Combine(inactive, "not-granted.wav");
        File.WriteAllBytes(privateFile, new byte[] { 0, 1, 2 });
        try
        {
            string link = Path.Combine(main, "other-library");
            Directory.CreateSymbolicLink(link, inactive);
            string escapePath = Path.Combine(link, "not-granted.wav");
            Assert.True(File.Exists(escapePath));
            Assert.Equal(0, WindowsAppContainerLauncher.RunDeniedFileProbe(worker, escapePath));
        }
        finally
        {
            Directory.Delete(main, true);
            Directory.Delete(inactive, true);
        }
    }

    [Fact]
    public void SymbolicLinkCannotEscapeToPrivateUnsharedFile()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", ".."));
        var worker = Path.Combine(root, "TestResults", "sandbox-worker",
            "Editor.Security.Worker.exe");
        var directory = Path.Combine(Path.GetTempPath(), "poc003-path-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, "private.wav");
        var link = Path.Combine(directory, "untrusted-link.wav");
        try
        {
            File.WriteAllBytes(target, new byte[] { 17, 18, 19, 20 });
            File.CreateSymbolicLink(link, target);
            Assert.True(File.Exists(link), "Symlink must exist for the adversarial test.");
            var exit = WindowsAppContainerLauncher.RunDeniedFileProbe(worker, link);
            Assert.Equal(0, exit);
        }
        finally
        {
            if (File.Exists(link)) File.Delete(link);
            if (File.Exists(target)) File.Delete(target);
            if (Directory.Exists(directory)) Directory.Delete(directory);
        }
    }
}
