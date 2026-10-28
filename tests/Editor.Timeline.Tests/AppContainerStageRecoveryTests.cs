using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Editor.Security;
using Xunit;

namespace Editor.Timeline.Tests;

/// <summary>
/// Windows-only, real packaged AppContainer checks. The 64 MiB case proves
/// bounded-memory staging and cleanup; it is not a 10 GiB HDD benchmark.
/// </summary>
public sealed class AppContainerStageRecoveryTests
{
    private static string LeaseProof(Guid id) =>
        "MigaMelhore.EditorMultimedia.AppContainerStage.v1|" + id.ToString("N");

    private static string Worker()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", ".."));
        string worker = Path.Combine(root, "TestResults", "sandbox-worker",
            "Editor.Security.Worker.exe");
        Assert.True(File.Exists(worker), "The published AppContainer worker is mandatory.");
        return worker;
    }

    [Fact]
    public void Given64MiBRegisteredWav_WhenRealFfprobeInspects_ThenOriginalRemainsAndStagingIsClean()
    {
        if (!OperatingSystem.IsWindows()) return;
        string folder = Path.Combine(Path.GetTempPath(),
            "poc0032-large-stage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            const int audioBytes = 64 * 1024 * 1024;
            string wav = Path.Combine(folder, "large.wav");
            using (var writer = new BinaryWriter(File.Create(wav)))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + audioBytes);
                writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16); writer.Write((short)1);
                writer.Write((short)1); writer.Write(8000);
                writer.Write(8000); writer.Write((short)1);
                writer.Write((short)8);
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(audioBytes);
                writer.BaseStream.SetLength(44L + audioBytes);
            }
            string initialHash;
            using (var input = File.OpenRead(wav))
                initialHash = Convert.ToHexString(SHA256.HashData(input));

            var service = new IsolatedMediaProcessingService(folder, Guid.NewGuid(), Worker());
            long duration = service.InspectDurationTicks(wav);
            Assert.InRange(duration,
                TimeSpan.FromSeconds(audioBytes / 8000d - 0.1).Ticks,
                TimeSpan.FromSeconds(audioBytes / 8000d + 0.1).Ticks);
            using (var input = File.OpenRead(wav))
                Assert.Equal(initialHash, Convert.ToHexString(SHA256.HashData(input)));
            Assert.Empty(Directory.GetFiles(folder, ".editorworker-*", SearchOption.TopDirectoryOnly));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public void GivenStaleAndLiveLeases_WhenRecovering_ThenOnlyAbandonedWorkerInputIsRemoved()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = Path.Combine(Path.GetTempPath(),
            "poc0032-stage-lease-" + Guid.NewGuid().ToString("N"));
        string nested = Path.Combine(root, "subfolder");
        Directory.CreateDirectory(nested);
        try
        {
            Guid staleId = Guid.NewGuid(), liveId = Guid.NewGuid();
            string staleStem = ".editorworker-" + staleId.ToString("N");
            string liveStem = ".editorworker-" + liveId.ToString("N");
            string staleInput = Path.Combine(nested, staleStem + ".input.wav");
            string staleLease = Path.Combine(nested, staleStem + ".lease");
            string liveInput = Path.Combine(nested, liveStem + ".input.wav");
            string liveLease = Path.Combine(nested, liveStem + ".lease");
            File.WriteAllText(staleInput, "crash copy");
            File.WriteAllText(staleLease, LeaseProof(staleId));
            File.WriteAllText(liveInput, "live worker");
            // A user-created GUID-looking file without our persisted lease
            // proof must never be treated as an application-owned temporary.
            Guid untrustedId = Guid.NewGuid();
            string untrustedStem = ".editorworker-" + untrustedId.ToString("N");
            string untrustedInput = Path.Combine(nested, untrustedStem + ".input.wav");
            string untrustedLease = Path.Combine(nested, untrustedStem + ".lease");
            File.WriteAllText(untrustedInput, "preserve original");
            File.WriteAllText(untrustedLease, "not an application lease");
            string unrelated = Path.Combine(nested, "do-not-delete.wav");
            File.WriteAllText(unrelated, "user media");
            File.WriteAllText(liveLease, LeaseProof(liveId));
            using (var heldLease = new FileStream(liveLease, FileMode.Open,
                       FileAccess.ReadWrite, FileShare.None))
            {
                var service = new IsolatedMediaProcessingService(root, Guid.NewGuid(), Worker());
                Assert.False(File.Exists(staleInput));
                Assert.False(File.Exists(staleLease));
                Assert.True(File.Exists(liveInput));
                Assert.True(File.Exists(liveLease));
                Assert.Equal(0, service.RecoverAbandonedInputs());
                Assert.Equal("user media", File.ReadAllText(unrelated));
                Assert.True(File.Exists(untrustedInput));
                Assert.True(File.Exists(untrustedLease));
            }
            var another = new IsolatedMediaProcessingService(root, Guid.NewGuid(), Worker());
            Assert.Equal(0, another.RecoverAbandonedInputs()); // Constructor already reclaimed.
            Assert.False(File.Exists(liveInput));
            Assert.False(File.Exists(liveLease));
            Assert.True(File.Exists(unrelated));
            Assert.Equal("preserve original", File.ReadAllText(untrustedInput));
            Assert.True(File.Exists(untrustedLease));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task GivenControllerKilledWithoutDispose_WhenRestarted_ThenStagedCopyIsReclaimed()
    {
        if (!OperatingSystem.IsWindows()) return;
        string folder = Path.Combine(Path.GetTempPath(),
            "poc0032-owner-crash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        Guid id = Guid.NewGuid();
        string stem = ".editorworker-" + id.ToString("N");
        string stage = Path.Combine(folder, stem + ".input.wav");
        string marker = Path.Combine(folder, stem + ".lease");
        static string Quote(string path) => "'" + path.Replace("'", "''") + "'";
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add(
            "[IO.File]::WriteAllText(" + Quote(stage) + ", 'partial'); " +
            "[IO.File]::WriteAllText(" + Quote(marker) + ", " + Quote(LeaseProof(id)) + "); " +
            "$lease = [IO.File]::Open(" + Quote(marker) +
            ", [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None); " +
            "[Console]::WriteLine('READY'); Start-Sleep -Seconds 30");
        using var owner = Process.Start(start)
            ?? throw new InvalidOperationException("Cannot start crash-injection process.");
        try
        {
            Assert.Equal("READY",
                await owner.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.True(File.Exists(stage));
            var service = new IsolatedMediaProcessingService(folder, Guid.NewGuid(), Worker());
            Assert.True(File.Exists(stage)); // Live lease must never be cleared.
            owner.Kill(entireProcessTree: true);
            Assert.True(owner.WaitForExit(5000));
            Assert.Equal(1, service.RecoverAbandonedInputs());
            Assert.False(File.Exists(stage));
            Assert.False(File.Exists(marker));
        }
        finally
        {
            if (!owner.HasExited) owner.Kill(entireProcessTree: true);
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }
    [Fact]
    public void GivenRealDecoderInAppContainer_WhenJobIsForceClosed_ThenBothProcessesStopAndInputIsCleaned()
    {
        if (!OperatingSystem.IsWindows()) return;
        string folder = Path.Combine(Path.GetTempPath(),
            "poc0032-forced-codec-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            const int sampleCount = 24000; // 3 seconds at 8 kHz, slow-play test.
            string wav = Path.Combine(folder, "kill.wav");
            using (var writer = new BinaryWriter(File.Create(wav)))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + sampleCount);
                writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16); writer.Write((short)1);
                writer.Write((short)1); writer.Write(8000);
                writer.Write(8000); writer.Write((short)1);
                writer.Write((short)8);
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(sampleCount);
                writer.BaseStream.SetLength(44L + sampleCount);
            }
            Guid library = Guid.NewGuid(), task = Guid.NewGuid(), resource = Guid.NewGuid();
            var registry = new AccessGrantRegistry(library);
            var catalog = new AuthorizedMediaCatalog(folder, registry);
            catalog.Register(resource, wav);
            var grant = registry.Issue(task, library, resource,
                MediaAccessOperation.ReadChunk, TimeSpan.FromMinutes(2));
            var request = new WorkerRequest(WorkerRequest.CurrentVersion, Guid.NewGuid(),
                task, library, resource, grant, MediaAccessOperation.ReadChunk);
            Assert.Equal(0, WindowsAppContainerLauncher.RunAuthorizedInterruptedDecodeProbe(
                Worker(), catalog, request));
            Assert.True(File.Exists(wav));
            Assert.Empty(Directory.GetFiles(folder, ".editorworker-*", SearchOption.TopDirectoryOnly));
            registry.RevokeTask(task);
            Assert.Throws<UnauthorizedAccessException>(() =>
                WindowsAppContainerLauncher.RunAuthorizedInterruptedDecodeProbe(
                    Worker(), catalog, request));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

}
