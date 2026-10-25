using Editor.Timeline;
using Xunit;

namespace Editor.Timeline.Tests;

public sealed class ProjectMediaCatalogTests
{
    private static MediaTime T(decimal seconds) => MediaTime.FromSeconds(seconds);

    private sealed class TempSource : IDisposable
    {
        public string Folder { get; } = Path.Combine(Path.GetTempPath(), $"catalog-v63-{Guid.NewGuid():N}");
        public TempSource() => Directory.CreateDirectory(Folder);
        public MediaAsset Make(string name)
        {
            var path = Path.Combine(Folder, name);
            File.WriteAllBytes(path, [1, 2, 3, 4]);
            return MediaReferences.InspectFile(path);
        }
        public void Dispose() => Directory.Delete(Folder, true);
    }

    private static TimelineState SampleTimeline()
    {
        var video = new Clip(Guid.NewGuid(), Guid.NewGuid(),
            MediaKind.Video, 0, T(0), T(20));
        return new TimelineState(0, [video]);
    }

    [Fact]
    public void EmptyProjectCanBeSavedAndLoaded()
    {
        var project = new EditorProject(Guid.NewGuid(), "Projeto sem título",
            new TimelineState(0, Array.Empty<Clip>()));
        var restored = ProjectStore.Deserialize(ProjectStore.Serialize(project));
        Assert.Equal(project.Id, restored.Id);
        Assert.Empty(restored.Timeline.Clips);
        Assert.Empty(restored.MediaItems);
        Assert.True(ProjectStore.SameContent(project, restored));
    }

    [Fact]
    public void FirstVideoInEmptyProjectStartsAtZero()
    {
        using var tmp = new TempSource();
        var asset = tmp.Make("first.mp4");
        var state = new TimelineState(0, Array.Empty<Clip>());
        var proposed = MediaReferences.PlanClip(state, asset, T(42), Guid.NewGuid());
        Assert.Equal(0, proposed.Track);
        Assert.Equal(T(0), proposed.Start);
        var inserted = TimelineEngine.Insert(state, state.Version, proposed).State;
        Assert.Single(inserted.Clips);
        TimelineEngine.Validate(inserted);
    }

    [Fact]
    public void SameAudioAssetCanBeInsertedMultipleTimesWithoutDuplicateCatalogEntries()
    {
        using var tmp = new TempSource();
        var asset = tmp.Make("loop.wav");
        var session = new TimelineSession(SampleTimeline(), [asset]);
        var first = MediaReferences.PlanClip(session.Current, asset, T(0), Guid.NewGuid());
        session.Execute(s => TimelineEngine.Insert(s, s.Version, first));
        var second = MediaReferences.PlanClip(session.Current, asset, T(8), Guid.NewGuid());
        session.Execute(s => TimelineEngine.Insert(s, s.Version, second));

        var uses = session.Current.Clips.Where(c => c.MediaId == asset.Id).ToArray();
        Assert.Equal(2, uses.Length);
        Assert.NotEqual(uses[0].Id, uses[1].Id);
        Assert.Equal(asset, Assert.Single(session.Media));
        Assert.Equal(T(0), uses[0].Start);
        Assert.Equal(T(8), uses[1].Start);
    }

    [Fact]
    public void SameVideoCanBeReusedInMainTrackAfterExistingClips()
    {
        using var tmp = new TempSource();
        var asset = tmp.Make("movie.mp4");
        var state = SampleTimeline();
        var a = MediaReferences.PlanClip(state, asset, T(0), Guid.NewGuid());
        state = TimelineEngine.Insert(state, state.Version, a).State;
        var b = MediaReferences.PlanClip(state, asset, T(1), Guid.NewGuid());
        state = TimelineEngine.Insert(state, state.Version, b).State;
        Assert.Equal(T(20), state.Find(a.Id).Start);
        Assert.Equal(T(25), state.Find(b.Id).Start);
        Assert.Equal(a.MediaId, b.MediaId);
        TimelineEngine.Validate(state);
    }

    [Fact]
    public void RemovingUsedAssetIsRejectedWithoutChangingStateOrHistory()
    {
        using var tmp = new TempSource();
        var asset = tmp.Make("used.mp3");
        var session = new TimelineSession(SampleTimeline(), [asset]);
        var clip = MediaReferences.PlanClip(session.Current, asset, T(2), Guid.NewGuid());
        session.Execute(s => TimelineEngine.Insert(s, s.Version, clip));
        var state = session.Current;
        Assert.Throws<MediaReferenceException>(() => session.RemoveUnusedMedia(asset.Id));
        Assert.Same(state, session.Current);
        Assert.Equal(asset, Assert.Single(session.Media));
        session.Undo();
        Assert.DoesNotContain(session.Current.Clips, c => c.Id == clip.Id);
    }

    [Fact]
    public void RemovingUnusedCatalogEntryDoesNotDeleteSourceFile()
    {
        using var tmp = new TempSource();
        var asset = tmp.Make("available.png");
        var session = new TimelineSession(SampleTimeline(), [asset]);
        session.RemoveUnusedMedia(asset.Id);
        Assert.Empty(session.Media);
        Assert.True(File.Exists(asset.SourcePath));
        Assert.Single(session.Current.Clips);
        session.Undo();
        Assert.Equal(asset, Assert.Single(session.Media));
        Assert.True(File.Exists(asset.SourcePath));
        session.Redo();
        Assert.Empty(session.Media);
        Assert.True(File.Exists(asset.SourcePath));
    }

    [Fact]
    public void MissingMediaCanBeRemovedFromCatalogIfUnused()
    {
        using var tmp = new TempSource();
        var asset = tmp.Make("missing.png");
        File.Delete(asset.SourcePath);
        var session = new TimelineSession(SampleTimeline(), [asset]);
        session.RemoveUnusedMedia(asset.Id);
        Assert.Empty(session.Media);
        session.Undo();
        Assert.False(MediaReferences.IsAvailable(Assert.Single(session.Media)));
    }

    [Fact]
    public void DeletingLastClipAllowsCatalogEntryToBeRemovedAndBothAreUndoable()
    {
        using var tmp = new TempSource();
        var asset = tmp.Make("sound.wav");
        var session = new TimelineSession(SampleTimeline(), [asset]);
        var clip = MediaReferences.PlanClip(session.Current, asset, T(3), Guid.NewGuid());
        session.Execute(s => TimelineEngine.Insert(s, s.Version, clip));
        session.Execute(s => TimelineEngine.DeleteSecondary(s, s.Version, clip.Id));
        session.RemoveUnusedMedia(asset.Id);

        Assert.Empty(session.Media);
        Assert.DoesNotContain(session.Current.Clips, c => c.Id == clip.Id);
        session.Undo(); // restore catalog
        Assert.Equal(asset, Assert.Single(session.Media));
        session.Undo(); // restore clip
        Assert.Equal(asset.Id, session.Current.Find(clip.Id).MediaId);
        session.Redo();
        session.Redo();
        Assert.Empty(session.Media);
    }

    [Fact]
    public void TwoClipsUsingSameAssetSurviveSaveLoad()
    {
        using var tmp = new TempSource();
        var asset = tmp.Make("speech.wav");
        var state = SampleTimeline();
        var clip1 = MediaReferences.PlanClip(state, asset, T(1), Guid.NewGuid());
        state = TimelineEngine.Insert(state, state.Version, clip1).State;
        var clip2 = MediaReferences.PlanClip(state, asset, T(12), Guid.NewGuid());
        state = TimelineEngine.Insert(state, state.Version, clip2).State;
        var saved = new EditorProject(Guid.NewGuid(), "Com repetição", state, [asset]);
        var loaded = ProjectStore.Deserialize(ProjectStore.Serialize(saved));
        Assert.True(ProjectStore.SameContent(saved, loaded));
        Assert.Equal(2, loaded.Timeline.Clips.Count(c => c.MediaId == asset.Id));
        Assert.Equal(asset, Assert.Single(loaded.MediaItems));
    }

    [Fact]
    public void RemovingUnknownMediaDoesNotAlterAnything()
    {
        using var tmp = new TempSource();
        var asset = tmp.Make("some.wav");
        var session = new TimelineSession(SampleTimeline(), [asset]);
        Assert.Throws<MediaReferenceException>(() => session.RemoveUnusedMedia(Guid.NewGuid()));
        Assert.Equal(asset, Assert.Single(session.Media));
        Assert.False(session.CanUndo);
    }
}
