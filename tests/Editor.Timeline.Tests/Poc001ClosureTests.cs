using System.Text.Json.Nodes;
using Editor.Timeline;
using Xunit;

namespace Editor.Timeline.Tests;

/// <summary>Single acceptance suite for source-aware, time-exact POC-001 timeline editing.</summary>
public sealed class Poc001ClosureTests
{
    private static MediaTime T(decimal seconds) => MediaTime.FromSeconds(seconds);
    private static readonly decimal SourceSeconds = 704.7m;

    private static MediaAsset File(MediaKind kind = MediaKind.Video, decimal seconds = SourceSeconds)
    {
        var ext = kind switch
        {
            MediaKind.Video => ".mp4",
            MediaKind.Audio => ".mp3",
            _ => ".png"
        };
        var name = $"{Guid.NewGuid():N}{ext}";
        var path = Path.Combine(Path.GetTempPath(), name);
        return new MediaAsset(Guid.NewGuid(), kind, name, path, 256000,
            kind == MediaKind.Image ? null : T(seconds).Ticks);
    }

    private static Clip ClipFrom(MediaAsset asset, decimal duration, decimal start = 0,
        decimal sourceStart = 0, int lane = 0) =>
        new(Guid.NewGuid(), asset.Id, asset.Kind, lane, T(start), T(duration),
            SourceStart: T(sourceStart), SourceRangeVerified: true);

    [Theory]
    [InlineData(0, "00:00:00")]
    [InlineData(5, "00:00:05")]
    [InlineData(704, "00:11:44")]
    [InlineData(704.7, "00:11:44,7")]
    [InlineData(3661.125, "01:01:01,125")]
    [InlineData(60.001, "00:01:00,001")]
    public void TimecodeDisplaysHoursMinutesSecondsAndMilliseconds(double seconds, string expected) =>
        Assert.Equal(expected, Timecode.Format(T((decimal)seconds)));

    [Fact]
    public void DisplayRoundingDoesNotMutateExactEngineClock()
    {
        var time = new MediaTime(7_047_123_456);
        Assert.Equal("00:11:44,712", Timecode.Format(time));
        Assert.Equal(7_047_123_456, time.Ticks);
    }

    [Fact]
    public void FullVideoDurationIsImportedAndKeptAsSourceRange()
    {
        var file = File();
        var state = new TimelineState(0, Array.Empty<Clip>());
        var proposed = MediaReferences.PlanClip(state, file, T(100), Guid.NewGuid());
        Assert.Equal(T(0), proposed.SourceStart);
        Assert.Equal(T(SourceSeconds), proposed.Duration);
        Assert.True(proposed.SourceRangeVerified);
        Assert.Equal(T(SourceSeconds), proposed.SourceEnd);
        var session = new TimelineSession(state);
        session.Execute(s => TimelineEngine.Insert(s, s.Version, proposed), [file]);
        Assert.Equal(T(SourceSeconds), session.Current.Duration);
        Assert.Equal(file, Assert.Single(session.Media));
    }

    [Fact]
    public void EveryAudioRequiresKnownDurationEvenWhenItsExtensionIsSupported()
    {
        foreach (var ext in new[] { ".mp3", ".wav", ".m4a", ".aac", ".flac", ".ogg" })
        {
            var old = File(MediaKind.Audio) with
            {
                SourcePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ext),
                DisplayName = "replaced.mp3",
                DurationTicks = null
            };
            var empty = new TimelineState(0, Array.Empty<Clip>());
            Assert.Throws<MediaReferenceException>(() =>
                MediaReferences.PlanClip(empty, old, T(0), Guid.NewGuid()));
            Assert.Empty(empty.Clips);
        }
    }

    [Fact]
    public void MeasuredAudioIsInsertedWithRealDurationNotFiveSeconds()
    {
        var audio = File(MediaKind.Audio, seconds: 163.42m);
        var session = new TimelineSession(new TimelineState(0, Array.Empty<Clip>()));
        var clip = MediaReferences.PlanClip(session.Current, audio, T(3), Guid.NewGuid());
        Assert.Equal(T(163.42m), clip.Duration);
        Assert.Equal(1, clip.Track);
        session.Execute(s => TimelineEngine.Insert(s, s.Version, clip), [audio]);
        Assert.Equal(T(163.42m), session.Current.Find(clip.Id).Duration);
    }

    [Fact]
    public void VideoWithUnknownDurationCannotBeInsertedOnMainTrack()
    {
        var video = File() with { DurationTicks = null };
        var current = new TimelineState(0, Array.Empty<Clip>());
        Assert.Throws<MediaReferenceException>(() =>
            MediaReferences.PlanClip(current, video, T(0), Guid.NewGuid()));
        Assert.Empty(current.Clips);
    }

    [Fact]
    public void SplitKeepsOriginalSourceIntervalsWithoutReplayingFirstFrames()
    {
        var video = File();
        var first = ClipFrom(video, 40, sourceStart: 30);
        var session = new TimelineSession(new TimelineState(0, [first]), [video]);
        var rightId = Guid.NewGuid();
        session.Execute(s => TimelineEngine.Split(s, s.Version, first.Id, T(14), rightId));
        var left = session.Current.Find(first.Id);
        var right = session.Current.Find(rightId);
        Assert.Equal(T(30), left.SourceStart);
        Assert.Equal(T(44), left.SourceEnd);
        Assert.Equal(T(44), right.SourceStart);
        Assert.Equal(T(70), right.SourceEnd);
        Assert.True(right.SourceRangeVerified);
        session.Undo();
        Assert.Equal(first, Assert.Single(session.Current.Clips));
        session.Redo();
        Assert.Equal(T(44), session.Current.Find(rightId).SourceStart);
    }

    [Fact]
    public void TrimMainFromLeftPreservesEarlierClipsAndRippleFollowingButMovesSourceIn()
    {
        var previousMedia = File(seconds: 9);
        var selectedMedia = File(seconds: 20);
        var lastMedia = File(seconds: 5);
        var first = ClipFrom(previousMedia, 9);
        var second = ClipFrom(selectedMedia, 12, start: 9);
        var third = ClipFrom(lastMedia, 5, start: 21);
        var session = new TimelineSession(new TimelineState(0, [first, second, third]),
            [previousMedia, selectedMedia, lastMedia]);
        session.Execute(s => TimelineEngine.TrimMainLeft(s, s.Version, second.Id, T(12)));
        Assert.Equal(first, session.Current.Find(first.Id));
        Assert.Equal(T(9), session.Current.Find(second.Id).Start);
        Assert.Equal(T(9), session.Current.Find(second.Id).Duration);
        Assert.Equal(T(3), session.Current.Find(second.Id).SourceStart);
        Assert.Equal(T(18), session.Current.Find(third.Id).Start);
        session.Undo();
        Assert.Equal(second, session.Current.Find(second.Id));
        Assert.Equal(third, session.Current.Find(third.Id));
    }

    [Fact]
    public void SecondaryLeftTrimPreservesRightEdgeAndChangesSourceOffset()
    {
        var video = File();
        var main = ClipFrom(video, 30);
        var audio = File(MediaKind.Audio, 30);
        var child = ClipFrom(audio, 10, start: 5, lane: 1) with
        {
            Association = AssociationKind.Positioned,
            ParentId = main.Id, ParentOffset = T(5)
        };
        var session = new TimelineSession(new TimelineState(0, [main, child]), [video, audio]);
        session.Execute(s => TimelineEngine.TrimSecondaryLeft(s, s.Version, child.Id, T(8)));
        var edited = session.Current.Find(child.Id);
        Assert.Equal(T(8), edited.Start);
        Assert.Equal(T(7), edited.Duration);
        Assert.Equal(T(3), edited.SourceStart);
        Assert.Equal(T(15), edited.End);
        Assert.Equal(T(8), edited.ParentOffset);
    }

    [Fact]
    public void ExtendingClipBeforeFirstSourceFrameIsRejectedAtomically()
    {
        var video = File(seconds: 15);
        var prior = new Clip(Guid.NewGuid(), Guid.NewGuid(), MediaKind.Video, 0, T(0), T(10));
        var first = ClipFrom(video, 8, start: 10);
        var session = new TimelineSession(new TimelineState(0, [prior, first]), [video]);
        Assert.Throws<MediaReferenceException>(() =>
            session.Execute(s => TimelineEngine.TrimMainLeft(s, s.Version, first.Id, T(8))));
        Assert.Equal(prior, session.Current.Find(prior.Id));
        Assert.Same(first, session.Current.Find(first.Id));
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void ExtendingPastEndOfMeasuredMediaIsRejectedAtomically()
    {
        var file = File(MediaKind.Audio, 12);
        var first = ClipFrom(file, 10, lane: 1);
        var original = new TimelineState(0, [first]);
        var session = new TimelineSession(original, [file]);
        Assert.Throws<MediaReferenceException>(() =>
            session.Execute(s => TimelineEngine.ResizeSecondary(s, s.Version, first.Id, T(13))));
        Assert.Same(original, session.Current);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void SourceEndIsCheckedAfterBothStartOffsetAndRightResize()
    {
        var file = File(MediaKind.Audio, 18);
        var first = ClipFrom(file, 8, sourceStart: 7, lane: 1);
        var session = new TimelineSession(new TimelineState(0, [first]), [file]);
        session.Execute(s => TimelineEngine.ResizeSecondary(s, s.Version, first.Id, T(11)));
        Assert.Equal(T(18), session.Current.Find(first.Id).SourceEnd);
        Assert.Throws<MediaReferenceException>(() =>
            session.Execute(s => TimelineEngine.ResizeSecondary(s, s.Version, first.Id, T(11.1m))));
        Assert.Equal(T(11), session.Current.Find(first.Id).Duration);
        Assert.True(session.CanUndo);
    }

    [Fact]
    public void SplitSecondaryAndDuplicateKeepCorrectAudioSourceRanges()
    {
        var audio = File(MediaKind.Audio, 100);
        var first = ClipFrom(audio, 20, start: 2, sourceStart: 35, lane: 2);
        var session = new TimelineSession(new TimelineState(0, [first]), [audio]);
        var rightId = Guid.NewGuid();
        session.Execute(s => TimelineEngine.Split(s, s.Version, first.Id, T(10), rightId));
        Assert.Equal(T(43), session.Current.Find(rightId).SourceStart);
        var copyId = Guid.NewGuid();
        session.Execute(s => TimelineEngine.DuplicateSecondary(s, s.Version, rightId, copyId));
        Assert.Equal(T(43), session.Current.Find(copyId).SourceStart);
        Assert.Equal(T(55), session.Current.Find(copyId).SourceEnd);
        Assert.NotEqual(rightId, copyId);
    }

    [Fact]
    public void StillImageCanBeTrimmedAndSplitWithoutInventingSourceOffset()
    {
        var image = File(MediaKind.Image);
        var clip = ClipFrom(image, 8, start: 5, lane: 1);
        var session = new TimelineSession(new TimelineState(0, [clip]), [image]);
        session.Execute(s => TimelineEngine.TrimSecondaryLeft(s, s.Version, clip.Id, T(7)));
        Assert.Equal(T(0), session.Current.Find(clip.Id).SourceStart);
        var right = Guid.NewGuid();
        session.Execute(s => TimelineEngine.Split(s, s.Version, clip.Id, T(10), right));
        Assert.Equal(T(0), session.Current.Find(right).SourceStart);
    }

    [Fact]
    public void MoveAndReorderDoNotChangeWhichFramesArePlayed()
    {
        var aMedia = File();
        var bMedia = File();
        var a = ClipFrom(aMedia, 20, sourceStart: 35);
        var b = ClipFrom(bMedia, 15, start: 20, sourceStart: 70);
        var session = new TimelineSession(new TimelineState(0, [a, b]), [aMedia, bMedia]);
        session.Execute(s => TimelineEngine.ReorderMain(s, s.Version, b.Id, 0));
        Assert.Equal(T(70), session.Current.Find(b.Id).SourceStart);
        Assert.Equal(T(35), session.Current.Find(a.Id).SourceStart);
        Assert.Equal(T(15), session.Current.Find(a.Id).Start);
    }

    [Fact]
    public void LegacyClipCanRefreshShorterMeasuredSourceWithoutDeletingItsEdit()
    {
        var file = File(MediaKind.Audio, 3);
        var legacy = ClipFrom(file, 5) with { SourceRangeVerified = false, Track = 1 };
        var session = new TimelineSession(new TimelineState(0, [legacy]),
            [file with { DurationTicks = null }]);
        var refreshed = file;
        session.Execute(s => new CommandResult(
            s with { Version = s.Version + 1 }, Array.Empty<Guid>()), [refreshed]);
        Assert.Equal(T(5), session.Current.Find(legacy.Id).Duration);
        Assert.False(session.Current.Find(legacy.Id).SourceRangeVerified);
        var project = new EditorProject(Guid.NewGuid(), "Legado", session.Current, session.Media);
        Assert.Equal(T(5), Assert.Single(ProjectStore.Deserialize(ProjectStore.Serialize(project)).Timeline.Clips).Duration);
        Assert.Throws<MediaReferenceException>(() =>
            session.Execute(s => TimelineEngine.ResizeSecondary(s, s.Version, legacy.Id, T(4))));
        Assert.Equal(T(5), session.Current.Find(legacy.Id).Duration);
    }

    [Fact]
    public void ProjectFormatV4PersistsSourceOffsetAndValidatedFlag()
    {
        var file = File();
        var clip = ClipFrom(file, 120, sourceStart: 34.5m);
        var original = new EditorProject(Guid.NewGuid(), "POC-001",
            new TimelineState(0, [clip]), [file]);
        var json = ProjectStore.Serialize(original);
        Assert.Contains("\"formatVersion\": 4", json);
        Assert.Contains("\"sourceStartTicks\"", json);
        var loaded = ProjectStore.Deserialize(json);
        Assert.True(ProjectStore.SameContent(original, loaded));
        Assert.Equal(T(34.5m), Assert.Single(loaded.Timeline.Clips).SourceStart);
        Assert.True(loaded.Timeline.Clips[0].SourceRangeVerified);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void LegacyProjectsLoadWithoutSourceOffset(int legacyFormat)
    {
        var file = File();
        var clip = ClipFrom(file, 20);
        var project = new EditorProject(Guid.NewGuid(), "Antes", new TimelineState(0, [clip]), [file]);
        var json = JsonNode.Parse(ProjectStore.Serialize(project))!.AsObject();
        json["formatVersion"] = legacyFormat;
        json["clips"]![0]!.AsObject().Remove("sourceStartTicks");
        json["clips"]![0]!.AsObject().Remove("sourceRangeVerified");
        if (legacyFormat == 1) json.Remove("media");
        if (legacyFormat == 2)
        {
            foreach (var item in json["media"]!.AsArray())
            {
                item!.AsObject().Remove("durationTicks");
                item.AsObject().Remove("pixelWidth");
                item.AsObject().Remove("pixelHeight");
            }
        }
        var loaded = ProjectStore.Deserialize(json.ToJsonString());
        Assert.Equal(T(0), Assert.Single(loaded.Timeline.Clips).SourceStart);
        Assert.False(loaded.Timeline.Clips[0].SourceRangeVerified);
    }

    [Fact]
    public void FfprobeSecondsUseInvariantCultureAndExact100NsClock()
    {
        Assert.Equal(T(704.7m).Ticks, FfprobeDuration.ParseDurationSeconds("704.700000"));
        Assert.Equal(T(11.125m).Ticks, FfprobeDuration.ParseDurationSeconds("11.125"));
        Assert.Equal(T(1m).Ticks, FfprobeDuration.ParseDurationSeconds("1.000000"));
        Assert.Null(FfprobeDuration.ParseDurationSeconds("N/A"));
        Assert.Null(FfprobeDuration.ParseDurationSeconds("0"));
        Assert.Null(FfprobeDuration.ParseDurationSeconds("-1"));
        Assert.Null(FfprobeDuration.ParseDurationSeconds("1,2"));
        Assert.Null(FfprobeDuration.ParseDurationSeconds("999999999999999999999"));
    }
}
