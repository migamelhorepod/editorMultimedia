using Editor.Security;
using Xunit;

namespace Editor.Timeline.Tests;

public sealed class WpfPackagedWorkerTests
{
    [Fact]
    public void GivenPackagedWpfWorker_WhenMediaIsInspected_ThenRealSandboxedCodecRuns()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", ".."));
        string bundle = Path.Combine(root, "src", "Editor.Timeline.Wpf",
            "bin", "Release", "net10.0-windows", "worker");
        string worker = Path.Combine(bundle, "Editor.Security.Worker.exe");
        Assert.True(File.Exists(worker), "The WPF bundle must include the isolated worker executable.");
        Assert.True(File.Exists(Path.Combine(bundle, "ffprobe.exe")));
        Assert.True(File.Exists(Path.Combine(bundle, "ffmpeg.exe")));
        string folder = Path.Combine(Path.GetTempPath(),
            "wpf-worker-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string wav = Path.Combine(folder, "smoke.wav");
            const int sampleCount = 8000;
            using (var stream = File.Create(wav))
            using (var writer = new BinaryWriter(stream))
            {
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
            var service = new IsolatedMediaProcessingService(folder, Guid.NewGuid(), worker);
            Assert.InRange(service.InspectDurationTicks(wav),
                TimeSpan.FromSeconds(0.99).Ticks, TimeSpan.FromSeconds(1.01).Ticks);
            service.DecodeToNull(wav);
            Assert.Empty(Directory.GetFiles(folder, ".editorworker-*"));
            Assert.True(File.Exists(wav));
        }
        finally { Directory.Delete(folder, true); }
    }
}
