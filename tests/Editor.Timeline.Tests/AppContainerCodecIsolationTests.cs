using System.Diagnostics;
using Editor.Security;
using Xunit;

namespace Editor.Timeline.Tests;

public sealed class AppContainerCodecIsolationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RealCodecCanReadOnlyExplicitSyntheticWavInAppContainer(bool useFfmpeg)
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", ".."));
        string worker = Path.Combine(root, "TestResults", "sandbox-worker",
            "Editor.Security.Worker.exe");
        string tool = Path.Combine(Path.GetDirectoryName(worker)!,
            useFfmpeg ? "ffmpeg.exe" : "ffprobe.exe");
        Assert.True(File.Exists(worker));
        Assert.True(File.Exists(tool), $"Codec not installed in sandbox bundle: {tool}");
        string wav = Path.Combine(Path.GetTempPath(),
            "editor-poc003-granted-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            using (var stream = File.Create(wav))
            using (var writer = new BinaryWriter(stream))
            {
                const int sampleCount = 800;
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + sampleCount);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16); writer.Write((short)1);
                writer.Write((short)1); writer.Write(8000);
                writer.Write(8000); writer.Write((short)1);
                writer.Write((short)8);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                writer.Write(sampleCount);
                writer.Write(Enumerable.Repeat((byte)128, sampleCount).ToArray());
            }
            var sid = WindowsAppContainerPreflight.DerivePackageSid();
            var start = new ProcessStartInfo("icacls.exe") { UseShellExecute = false };
            start.ArgumentList.Add(wav);
            start.ArgumentList.Add("/grant");
            start.ArgumentList.Add("*" + sid + ":R");
            using var acl = Process.Start(start)!;
            Assert.True(acl.WaitForExit(5000));
            Assert.Equal(0, acl.ExitCode);
            int result = WindowsAppContainerLauncher.RunCodecIsolationProbe(worker, wav, useFfmpeg);
            Assert.True(result == 0, $"Sandboxed codec returned {result}.");
        }
        finally
        {
            if (File.Exists(wav)) File.Delete(wav);
        }
    }
}
