using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;
using Editor.Timeline;
using Xunit;

namespace Editor.Timeline.Tests;

/// <summary>Exercises bounded header parsing without external FFmpeg or decoders.</summary>
public sealed class MediaMetadataTests
{
    private sealed class TemporaryFiles : IDisposable
    {
        public string Folder { get; } = Path.Combine(Path.GetTempPath(), $"media-info-v64-{Guid.NewGuid():N}");
        public TemporaryFiles() => Directory.CreateDirectory(Folder);
        public string Create(string name, byte[] bytes)
        {
            var path = Path.Combine(Folder, name);
            File.WriteAllBytes(path, bytes);
            return path;
        }
        public void Dispose() => Directory.Delete(Folder, true);
    }

    private static byte[] BuildWave(int sampleRate = 8000, int sampleFrames = 8000,
        ushort channels = 1, ushort bits = 16, ushort format = 1, bool junk = false)
    {
        var blockAlign = (ushort)(channels * (bits / 8));
        var samplesLength = checked(sampleFrames * blockAlign);
        using var output = new MemoryStream();
        using (var writer = new BinaryWriter(output, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(checked((uint)(36 + samplesLength + (junk ? 12 : 0))));
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            if (junk)
            {
                writer.Write(Encoding.ASCII.GetBytes("JUNK"));
                writer.Write(4);
                writer.Write([1, 2, 3, 4]);
            }
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write(format);
            writer.Write(channels);
            writer.Write(sampleRate);
            writer.Write(sampleRate * blockAlign);
            writer.Write(blockAlign);
            writer.Write(bits);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(samplesLength);
            writer.Write(new byte[samplesLength]);
        }
        return output.ToArray();
    }

    private static byte[] PngHeader(uint width, uint height, bool validSignature = true)
    {
        var bytes = new byte[24];
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        signature.CopyTo(bytes);
        if (!validSignature) bytes[0] = 0;
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8, 4), 13);
        Encoding.ASCII.GetBytes("IHDR").CopyTo(bytes, 12);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16, 4), width);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20, 4), height);
        return bytes;
    }

    [Fact] public void PcmWaveUsesActualDurationForNewTimelineClip()
    {
        using var temp = new TemporaryFiles();
        var asset = MediaReferences.InspectFile(temp.Create("one.wav", BuildWave()));
        Assert.Equal(MediaTime.FromSeconds(1).Ticks, asset.DurationTicks);
        Assert.Null(asset.PixelWidth);
        var state = new TimelineState(0, Array.Empty<Clip>());
        var clip = MediaReferences.PlanClip(state, asset, MediaTime.Zero, Guid.NewGuid());
        Assert.Equal(MediaTime.FromSeconds(1), clip.Duration);
        Assert.Equal(1, clip.Track);
    }

    [Fact] public void WavStereoWithAdditionalChunkProvidesCorrectDuration()
    {
        using var temp = new TemporaryFiles();
        var bytes = BuildWave(sampleRate: 48000, sampleFrames: 96000,
            channels: 2, bits: 24, junk: true);
        var asset = MediaReferences.InspectFile(temp.Create("music.WAV", bytes));
        Assert.Equal(MediaTime.FromSeconds(2).Ticks, asset.DurationTicks);
    }

    [Fact] public void IeeeFloatWaveIsSupported()
    {
        using var temp = new TemporaryFiles();
        var asset = MediaReferences.InspectFile(temp.Create("float.wav",
            BuildWave(sampleRate: 8000, sampleFrames: 4000, bits: 32, format: 3)));
        Assert.Equal(MediaTime.FromSeconds(0.5m).Ticks, asset.DurationTicks);
    }

    [Fact] public void UnrecognizedOrTruncatedWavUsesClearlyProvisionalDuration()
    {
        using var temp = new TemporaryFiles();
        var bad = MediaReferences.InspectFile(temp.Create("bad.wav", [1, 2, 3, 4]));
        Assert.Null(bad.DurationTicks);
        var unsupported = MediaReferences.InspectFile(temp.Create("compressed.wav",
            BuildWave(format: 7)));
        Assert.Null(unsupported.DurationTicks);
        var state = new TimelineState(0, Array.Empty<Clip>());
        var planned = MediaReferences.PlanClip(state, bad, MediaTime.Zero, Guid.NewGuid());
        Assert.Equal(MediaTime.FromSeconds(5), planned.Duration);
    }

    [Fact] public void TruncatedRiffChunkDoesNotCrashImport()
    {
        using var temp = new TemporaryFiles();
        var bytes = BuildWave();
        // RIFF declares more data than the real file contains.
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), 1000000);
        var asset = MediaReferences.InspectFile(temp.Create("cut.wav", bytes));
        Assert.Null(asset.DurationTicks);
    }

    [Fact] public void PngDimensionsAreRecognizedFromHeaderWithoutDecoding()
    {
        using var temp = new TemporaryFiles();
        var asset = MediaReferences.InspectFile(
            temp.Create("cover.png", PngHeader(1920, 1080)));
        Assert.Equal(1920, asset.PixelWidth);
        Assert.Equal(1080, asset.PixelHeight);
        Assert.Null(asset.DurationTicks);
        var clip = MediaReferences.PlanClip(
            new TimelineState(0, Array.Empty<Clip>()), asset, MediaTime.Zero, Guid.NewGuid());
        Assert.Equal(MediaTime.FromSeconds(4), clip.Duration);
    }

    [Theory]
    [InlineData(0u, 500u)]
    [InlineData(100001u, 500u)]
    [InlineData(100u, 0u)]
    public void InvalidPngDimensionsAreUnknown(uint width, uint height)
    {
        using var temp = new TemporaryFiles();
        var asset = MediaReferences.InspectFile(
            temp.Create("wrong.png", PngHeader(width, height)));
        Assert.Null(asset.PixelWidth);
        Assert.Null(asset.PixelHeight);
    }

    [Fact] public void CorruptPngSignatureIsNotClaimedAsValidDimensions()
    {
        using var temp = new TemporaryFiles();
        var asset = MediaReferences.InspectFile(
            temp.Create("bad.png", PngHeader(1920, 1080, validSignature: false)));
        Assert.Null(asset.PixelWidth);
    }

    [Fact] public void ProjectFormatV3RoundTripPreservesMeasurements()
    {
        using var temp = new TemporaryFiles();
        var wave = MediaReferences.InspectFile(temp.Create("audio.wav", BuildWave()));
        var png = MediaReferences.InspectFile(temp.Create("image.png", PngHeader(640, 360)));
        var timeline = new TimelineState(0, Array.Empty<Clip>());
        var project = new EditorProject(Guid.NewGuid(), "Format v3", timeline, [wave, png]);
        var json = ProjectStore.Serialize(project);
        Assert.Contains("\"formatVersion\": 3", json);
        var loaded = ProjectStore.Deserialize(json);
        Assert.True(ProjectStore.SameContent(project, loaded));
        Assert.Equal(MediaTime.FromSeconds(1).Ticks,
            Assert.Single(loaded.MediaItems, m => m.Kind == MediaKind.Audio).DurationTicks);
        Assert.Equal(640, Assert.Single(loaded.MediaItems,
            m => m.Kind == MediaKind.Image).PixelWidth);
    }

    [Fact] public void ExistingV2ProjectOpensWithoutArtificiallyDiscoveringMetadata()
    {
        using var temp = new TemporaryFiles();
        var wave = MediaReferences.InspectFile(temp.Create("audio.wav", BuildWave()));
        var project = new EditorProject(Guid.NewGuid(), "V2 antigo",
            new TimelineState(0, Array.Empty<Clip>()), [wave]);
        var json = JsonNode.Parse(ProjectStore.Serialize(project))!.AsObject();
        json["formatVersion"] = 2;
        foreach (var item in json["media"]!.AsArray())
        {
            var media = item!.AsObject();
            media.Remove("durationTicks");
            media.Remove("pixelWidth");
            media.Remove("pixelHeight");
        }
        var loaded = ProjectStore.Deserialize(json.ToJsonString());
        Assert.Null(Assert.Single(loaded.MediaItems).DurationTicks);
        Assert.Equal(wave.Id, loaded.MediaItems[0].Id);
    }

    [Fact] public void RelinkRefreshesMetadataWithoutChangingExistingTimelineEdits()
    {
        using var temp = new TemporaryFiles();
        var a = MediaReferences.InspectFile(temp.Create("one.wav", BuildWave(sampleFrames: 8000)));
        var b = temp.Create("two.wav", BuildWave(sampleFrames: 24000));
        var clip = new Clip(Guid.NewGuid(), a.Id, MediaKind.Audio, 1,
            MediaTime.FromSeconds(0), MediaTime.FromSeconds(0.5m));
        var session = new TimelineSession(new TimelineState(0, [clip]), [a]);
        session.RelinkMedia(a.Id, b);
        Assert.Equal(MediaTime.FromSeconds(3).Ticks, Assert.Single(session.Media).DurationTicks);
        Assert.Equal(MediaTime.FromSeconds(0.5m), session.Current.Find(clip.Id).Duration);
        session.Undo();
        Assert.Equal(MediaTime.FromSeconds(1).Ticks, Assert.Single(session.Media).DurationTicks);
    }

    [Fact] public void CorruptMetadataInProjectIsRejected()
    {
        using var temp = new TemporaryFiles();
        var png = MediaReferences.InspectFile(temp.Create("image.png", PngHeader(320, 240)));
        var project = new EditorProject(Guid.NewGuid(), "Corrupt",
            new TimelineState(0, Array.Empty<Clip>()), [png]);
        var json = JsonNode.Parse(ProjectStore.Serialize(project))!.AsObject();
        json["media"]![0]!["pixelWidth"] = -7;
        Assert.Throws<ProjectFileException>(() => ProjectStore.Deserialize(json.ToJsonString()));
    }
}
