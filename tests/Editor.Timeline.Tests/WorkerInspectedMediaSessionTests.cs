using Editor.Timeline;
using Xunit;

namespace Editor.Timeline.Tests;

public sealed class WorkerInspectedMediaSessionTests
{
    private static MediaTime T(decimal seconds) => MediaTime.FromSeconds(seconds);
    private static MediaAsset Asset(string file, MediaKind kind, int seconds) =>
        new(Guid.NewGuid(), kind, file, Path.Combine(Path.GetTempPath(), file),
            2048, T(seconds).Ticks);

    [Fact]
    public void GivenWorkerMeasuredDuration_WhenRefreshed_ThenUndoRestoresPreviousMetadata()
    {
        var asset = Asset("worker-refresh.wav", MediaKind.Audio, 5);
        var session = new TimelineSession(new TimelineState(0, []), [asset]);
        session.RefreshMediaMetadataInspected(asset.Id, asset with { DurationTicks = T(10).Ticks });
        Assert.Equal(T(10).Ticks, Assert.Single(session.Media).DurationTicks);
        session.Undo();
        Assert.Equal(T(5).Ticks, Assert.Single(session.Media).DurationTicks);
        session.Redo();
        Assert.Equal(T(10).Ticks, Assert.Single(session.Media).DurationTicks);
    }

    [Fact]
    public void GivenWorkerVerifiedReplacement_WhenRelinked_ThenOriginalIdIsPreserved()
    {
        var asset = Asset("worker-before.wav", MediaKind.Audio, 5);
        var replacement = Asset("worker-after.wav", MediaKind.Audio, 8);
        var session = new TimelineSession(new TimelineState(0, []), [asset]);
        session.RelinkMediaInspected(asset.Id, replacement);
        var current = Assert.Single(session.Media);
        Assert.Equal(asset.Id, current.Id);
        Assert.Equal(replacement.SourcePath, current.SourcePath);
        session.Undo();
        Assert.Equal(asset.SourcePath, Assert.Single(session.Media).SourcePath);
    }

    [Fact]
    public void GivenWorkerVerifiedLongerVideo_WhenRestored_ThenUndoRestoresClipAndMetadataTogether()
    {
        var asset = Asset("worker-fit.mp4", MediaKind.Video, 5);
        var clip = new Clip(Guid.NewGuid(), asset.Id, MediaKind.Video, 0, T(0), T(5));
        var session = new TimelineSession(new TimelineState(0, [clip]), [asset]);
        session.FitClipToMeasuredSource(clip.Id, asset with { DurationTicks = T(10).Ticks });
        Assert.Equal(T(10), Assert.Single(session.Current.Clips).Duration);
        Assert.Equal(T(10).Ticks, Assert.Single(session.Media).DurationTicks);
        session.Undo();
        Assert.Equal(T(5), Assert.Single(session.Current.Clips).Duration);
        Assert.Equal(T(5).Ticks, Assert.Single(session.Media).DurationTicks);
    }
}
