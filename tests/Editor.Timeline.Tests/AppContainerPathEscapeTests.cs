using Editor.Security;
using Xunit;

namespace Editor.Timeline.Tests;

public sealed class AppContainerPathEscapeTests
{
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
