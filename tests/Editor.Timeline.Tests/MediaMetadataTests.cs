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
        Assert.Contains("\"formatVersion\": 4", json);
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

    [Fact] public void ExistingCatalogEntryCanRefreshMeasurementsAndUndo()
    {
        using var temp = new TemporaryFiles();
        var wave = MediaReferences.InspectFile(temp.Create("old.wav", BuildWave()));
        var legacy = wave with { DurationTicks = null };
        var timeline = new TimelineState(0,
            [new Clip(Guid.NewGuid(), wave.Id, MediaKind.Audio, 1,
                MediaTime.Zero, MediaTime.FromSeconds(5))]);
        var session = new TimelineSession(timeline, [legacy]);

        session.RefreshMediaMetadata(legacy.Id);
        Assert.Equal(MediaTime.FromSeconds(1).Ticks,
            Assert.Single(session.Media).DurationTicks);
        Assert.Equal(MediaTime.FromSeconds(5), session.Current.Clips[0].Duration);
        Assert.True(session.CanUndo);

        session.Undo();
        Assert.Null(Assert.Single(session.Media).DurationTicks);
        Assert.Same(timeline, session.Current);

        session.Redo();
        Assert.Equal(MediaTime.FromSeconds(1).Ticks,
            Assert.Single(session.Media).DurationTicks);
    }

    [Fact] public void RefreshingUnchangedMeasurementsCreatesNoHistoryEntry()
    {
        using var temp = new TemporaryFiles();
        var measured = MediaReferences.InspectFile(temp.Create("one.wav", BuildWave()));
        var session = new TimelineSession(new TimelineState(0, Array.Empty<Clip>()), [measured]);
        session.RefreshMediaMetadata(measured.Id);
        Assert.False(session.CanUndo);
        Assert.Equal(measured, Assert.Single(session.Media));
    }

    [Fact] public void RefreshDoesNotReplaceMissingOriginalOrModifyTimeline()
    {
        using var temp = new TemporaryFiles();
        var asset = MediaReferences.InspectFile(temp.Create("gone.wav", BuildWave()));
        File.Delete(asset.SourcePath);
        var initial = new TimelineState(0, Array.Empty<Clip>());
        var session = new TimelineSession(initial, [asset]);
        Assert.Throws<MediaReferenceException>(() => session.RefreshMediaMetadata(asset.Id));
        Assert.Same(initial, session.Current);
        Assert.Equal(asset, Assert.Single(session.Media));
        Assert.False(session.CanUndo);
    }

    [Fact] public void RefreshUnknownAssetFailsWithoutUndoEntry()
    {
        var session = new TimelineSession(new TimelineState(0, Array.Empty<Clip>()));
        Assert.Throws<MediaReferenceException>(() => session.RefreshMediaMetadata(Guid.NewGuid()));
        Assert.False(session.CanUndo);
    }


    private static byte[] IsoBox(string type, byte[] payload, bool extended = false)
    {
        var headerSize = extended ? 16 : 8;
        var bytes = new byte[headerSize + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0, 4),
            extended ? 1u : (uint)bytes.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(bytes, 4);
        if (extended)
            BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(8, 8), (ulong)bytes.Length);
        payload.CopyTo(bytes, headerSize);
        return bytes;
    }

    private static byte[] Mp4File(uint timescale, ulong duration, bool version1 = false,
        bool movieAtEnd = false, bool extendedMoov = false, bool ftyp = true)
    {
        var mvhd = new byte[version1 ? 32 : 20];
        mvhd[0] = version1 ? (byte)1 : (byte)0;
        if (version1)
        {
            BinaryPrimitives.WriteUInt32BigEndian(mvhd.AsSpan(20, 4), timescale);
            BinaryPrimitives.WriteUInt64BigEndian(mvhd.AsSpan(24, 8), duration);
        }
        else
        {
            BinaryPrimitives.WriteUInt32BigEndian(mvhd.AsSpan(12, 4), timescale);
            BinaryPrimitives.WriteUInt32BigEndian(mvhd.AsSpan(16, 4), checked((uint)duration));
        }
        var moov = IsoBox("moov", IsoBox("mvhd", mvhd), extendedMoov);
        var branding = IsoBox("ftyp", "isom0000"u8.ToArray());
        var data = movieAtEnd ? IsoBox("mdat", new byte[256 * 1024]) : Array.Empty<byte>();
        var parts = new List<byte[]>();
        if (ftyp) parts.Add(branding);
        if (movieAtEnd) parts.Add(data);
        parts.Add(moov);
        return parts.SelectMany(x => x).ToArray();
    }

    [Fact]
    public void Mp4ElevenMinutesFortyFourSecondsIsInsertedWithoutAutomaticTrim()
    {
        using var temp = new TemporaryFiles();
        var video = MediaReferences.InspectFile(
            temp.Create("2026-10-11 20-42-34.mp4", Mp4File(1000, 704000)));
        Assert.Equal(MediaTime.FromSeconds(704).Ticks, video.DurationTicks);
        Assert.Equal(MediaKind.Video, video.Kind);
        var timeline = new TimelineState(0, Array.Empty<Clip>());
        var planned = MediaReferences.PlanClip(timeline, video,
            MediaTime.Zero, Guid.NewGuid());
        Assert.Equal(0, planned.Track);
        Assert.Equal(MediaTime.FromSeconds(704), planned.Duration);
        var inserted = TimelineEngine.Insert(timeline, timeline.Version, planned).State;
        Assert.Equal(MediaTime.FromSeconds(704), inserted.Duration);
        var saved = new EditorProject(Guid.NewGuid(), "Vídeo inteiro", inserted, [video]);
        var loaded = ProjectStore.Deserialize(ProjectStore.Serialize(saved));
        Assert.Equal(MediaTime.FromSeconds(704), Assert.Single(loaded.Timeline.Clips).Duration);
        Assert.Equal(video.DurationTicks, Assert.Single(loaded.MediaItems).DurationTicks);
    }

    [Theory]
    [InlineData(11000u, 1000u, 11)]
    [InlineData(704000u, 1000u, 704)]
    [InlineData(704500u, 1000u, 704)]
    public void VideoMp4DurationDoesNotDefaultToFiveSeconds(
        uint duration, uint timescale, int integralPart)
    {
        using var temp = new TemporaryFiles();
        var video = MediaReferences.InspectFile(temp.Create("video.mp4",
            Mp4File(timescale, duration)));
        Assert.NotNull(video.DurationTicks);
        Assert.True(video.DurationTicks > MediaTime.FromSeconds(5).Ticks);
        Assert.InRange(video.DurationTicks!.Value,
            MediaTime.FromSeconds(integralPart).Ticks,
            MediaTime.FromSeconds(integralPart + 1).Ticks);
    }

    [Fact]
    public void Mp4MoovAtEndAndLargeMdatAreHandledWithoutDecoding()
    {
        using var temp = new TemporaryFiles();
        var video = MediaReferences.InspectFile(temp.Create("huge.mp4",
            Mp4File(1000, 704000, movieAtEnd: true)));
        Assert.Equal(MediaTime.FromSeconds(704).Ticks, video.DurationTicks);
    }

    [Fact]
    public void ExtendedMoovBoxAndVersionOneMovieHeaderAreSupported()
    {
        using var temp = new TemporaryFiles();
        var video = MediaReferences.InspectFile(temp.Create("large.mp4",
            Mp4File(1000000, 704000000, version1: true, extendedMoov: true)));
        Assert.Equal(MediaTime.FromSeconds(704).Ticks, video.DurationTicks);
    }

    [Fact]
    public void MovWithoutFtypCanContainQuickTimeMovieHeader()
    {
        using var temp = new TemporaryFiles();
        var movie = MediaReferences.InspectFile(temp.Create("classic.mov",
            Mp4File(1000, 11000, ftyp: false)));
        Assert.Equal(MediaTime.FromSeconds(11).Ticks, movie.DurationTicks);
    }

    [Theory]
    [InlineData("broken.mp4")]
    [InlineData("broken.mov")]
    [InlineData("broken.mkv")]
    public void VideoWithoutMeasuredDurationIsNotSilentlyTrimmedToFiveSeconds(string filename)
    {
        using var temp = new TemporaryFiles();
        var video = MediaReferences.InspectFile(temp.Create(filename, [1, 2, 3, 4]));
        Assert.Null(video.DurationTicks);
        var state = new TimelineState(0, Array.Empty<Clip>());
        Assert.Throws<MediaReferenceException>(() =>
            MediaReferences.PlanClip(state, video, MediaTime.Zero, Guid.NewGuid()));
        Assert.Empty(state.Clips);
    }

    [Fact]
    public void InvalidMovieTimescaleAndTruncatedBoxCannotForgeDuration()
    {
        using var temp = new TemporaryFiles();
        var badScale = MediaReferences.InspectFile(temp.Create("scale.mp4",
            Mp4File(0, 11000)));
        Assert.Null(badScale.DurationTicks);
        var truncated = Mp4File(1000, 11000);
        truncated = truncated[..^7];
        var badHeader = MediaReferences.InspectFile(temp.Create("truncated.mp4", truncated));
        Assert.Null(badHeader.DurationTicks);
    }

    [Fact]
    public void MovieDurationCanBeMeasuredForExistingCatalogWithoutChangingTrims()
    {
        using var temp = new TemporaryFiles();
        var source = MediaReferences.InspectFile(
            temp.Create("video.mp4", Mp4File(1000, 704000)));
        var legacy = source with { DurationTicks = null };
        var clip = new Clip(Guid.NewGuid(), legacy.Id,
            MediaKind.Video, 0, MediaTime.Zero, MediaTime.FromSeconds(5));
        var before = new TimelineState(0, [clip]);
        var session = new TimelineSession(before, [legacy]);

        session.RefreshMediaMetadata(legacy.Id);
        Assert.Equal(MediaTime.FromSeconds(704).Ticks, Assert.Single(session.Media).DurationTicks);
        Assert.Equal(MediaTime.FromSeconds(5), session.Current.Find(clip.Id).Duration);
        session.Undo();
        Assert.Null(Assert.Single(session.Media).DurationTicks);
        session.Redo();
        Assert.Equal(source.DurationTicks, Assert.Single(session.Media).DurationTicks);

        var following = MediaReferences.PlanClip(session.Current, session.Media[0],
            MediaTime.Zero, Guid.NewGuid());
        Assert.Equal(MediaTime.FromSeconds(5), following.Start);
        Assert.Equal(MediaTime.FromSeconds(704), following.Duration);
    }

    [Fact]
    public void MainTrackAppendsFullDurationForSuccessiveVideos()
    {
        using var temp = new TemporaryFiles();
        var video = MediaReferences.InspectFile(temp.Create("long.mp4",
            Mp4File(1000, 704000)));
        var state = new TimelineState(0, Array.Empty<Clip>());
        for (var i = 0; i < 2; i++)
        {
            var next = MediaReferences.PlanClip(state, video,
                MediaTime.Zero, Guid.NewGuid());
            state = TimelineEngine.Insert(state, state.Version, next).State;
        }
        var ordered = state.Clips.OrderBy(x => x.Start.Ticks).ToArray();
        Assert.Equal(MediaTime.FromSeconds(704), ordered[0].Duration);
        Assert.Equal(MediaTime.FromSeconds(704), ordered[1].Start);
        Assert.Equal(MediaTime.FromSeconds(1408), state.Duration);
    }

    [Fact]
    public void VideoMetadataPersistedInFormatV3AndLegacyV2StillLoads()
    {
        using var temp = new TemporaryFiles();
        var source = MediaReferences.InspectFile(temp.Create("original.mp4",
            Mp4File(1000, 704000)));
        var project = new EditorProject(Guid.NewGuid(), "Video source",
            new TimelineState(0, Array.Empty<Clip>()), [source]);
        var json = JsonNode.Parse(ProjectStore.Serialize(project))!.AsObject();
        Assert.Equal(4, (int)json["formatVersion"]!);
        Assert.Equal(source.DurationTicks, (long)json["media"]![0]!["durationTicks"]!);
        var loaded = ProjectStore.Deserialize(json.ToJsonString());
        Assert.Equal(source, Assert.Single(loaded.MediaItems));

        json["formatVersion"] = 2;
        json["media"]![0]!.AsObject().Remove("durationTicks");
        json["media"]![0]!.AsObject().Remove("pixelWidth");
        json["media"]![0]!.AsObject().Remove("pixelHeight");
        var legacy = ProjectStore.Deserialize(json.ToJsonString());
        Assert.Null(Assert.Single(legacy.MediaItems).DurationTicks);
    }


    [Fact]
    public void RestoreFullVideoDurationRipplesNextMainAndAttachedMediaAndIsUndoable()
    {
        using var temp = new TemporaryFiles();
        var physical = MediaReferences.InspectFile(temp.Create(
            "2026-10-11 20-42-34.mp4", Mp4File(1000, 704000)));
        var oldReference = physical with { DurationTicks = null };
        var first = new Clip(Guid.NewGuid(), oldReference.Id, MediaKind.Video,
            0, MediaTime.Zero, MediaTime.FromSeconds(5));
        var second = new Clip(Guid.NewGuid(), Guid.NewGuid(), MediaKind.Video,
            0, MediaTime.FromSeconds(5), MediaTime.FromSeconds(9));
        var linked = new Clip(Guid.NewGuid(), Guid.NewGuid(), MediaKind.Audio,
            1, MediaTime.FromSeconds(7), MediaTime.FromSeconds(2),
            AssociationKind.Positioned, second.Id, MediaTime.FromSeconds(2));
        var independent = new Clip(Guid.NewGuid(), Guid.NewGuid(), MediaKind.Image,
            2, MediaTime.FromSeconds(50), MediaTime.FromSeconds(4));
        var state = new TimelineState(0, [first, second, linked, independent]);
        TimelineEngine.Validate(state);
        var session = new TimelineSession(state, [oldReference]);

        session.FitClipToSource(first.Id);
        Assert.Equal(MediaTime.FromSeconds(704), session.Current.Find(first.Id).Duration);
        Assert.Equal(MediaTime.FromSeconds(704), session.Current.Find(second.Id).Start);
        Assert.Equal(MediaTime.FromSeconds(706), session.Current.Find(linked.Id).Start);
        Assert.Equal(MediaTime.FromSeconds(50), session.Current.Find(independent.Id).Start);
        Assert.Equal(physical.DurationTicks, Assert.Single(session.Media).DurationTicks);
        TimelineEngine.Validate(session.Current);

        session.Undo();
        Assert.Same(state, session.Current);
        Assert.Null(Assert.Single(session.Media).DurationTicks);
        session.Redo();
        Assert.Equal(MediaTime.FromSeconds(704), session.Current.Find(first.Id).Duration);
        Assert.Equal(MediaTime.FromSeconds(706), session.Current.Find(linked.Id).Start);
    }

    [Fact]
    public void RestoreRealLengthOnAlreadyCorrectVideoIsNoOp()
    {
        using var temp = new TemporaryFiles();
        var source = MediaReferences.InspectFile(temp.Create("ok.mp4",
            Mp4File(1000, 704000)));
        var clip = new Clip(Guid.NewGuid(), source.Id, MediaKind.Video,
            0, MediaTime.Zero, MediaTime.FromSeconds(704));
        var session = new TimelineSession(new TimelineState(0, [clip]), [source]);
        session.FitClipToSource(clip.Id);
        Assert.False(session.CanUndo);
        Assert.Equal(source, Assert.Single(session.Media));
    }

    [Fact]
    public void RestoreDurationOfUnmeasurableVideoFailsWithoutModifyingClip()
    {
        using var temp = new TemporaryFiles();
        var bad = MediaReferences.InspectFile(temp.Create("unsupported.mkv", [1, 2, 3]));
        var clip = new Clip(Guid.NewGuid(), bad.Id, MediaKind.Video,
            0, MediaTime.Zero, MediaTime.FromSeconds(5));
        var state = new TimelineState(0, [clip]);
        var session = new TimelineSession(state, [bad]);
        Assert.Throws<MediaReferenceException>(() => session.FitClipToSource(clip.Id));
        Assert.Same(state, session.Current);
        Assert.Equal(bad, Assert.Single(session.Media));
        Assert.False(session.CanUndo);
    }

}
