using Editor.Timeline;
using Xunit;

namespace Editor.Timeline.Tests;

public sealed class MediaRelinkTests
{
    private static MediaTime T(decimal seconds) => MediaTime.FromSeconds(seconds);

    private sealed class TempFiles : IDisposable
    {
        public string Folder { get; } = Path.Combine(Path.GetTempPath(), $"editor-relink-{Guid.NewGuid():N}");
        public TempFiles() => Directory.CreateDirectory(Folder);
        public string Add(string name, byte[]? bytes = null)
        {
            var path = Path.Combine(Folder, name);
            File.WriteAllBytes(path, bytes ?? [1, 2, 3]);
            return path;
        }
        public void Dispose() => Directory.Delete(Folder, recursive: true);
    }

    private static TimelineState Timeline()
    {
        var a = new Clip(Guid.NewGuid(), Guid.NewGuid(), MediaKind.Video, 0, T(0), T(10));
        return new TimelineState(0, [a]);
    }

    [Fact]
    public void RelinkKeepsMediaIdAndAllExistingClipEdits()
    {
        using var temp = new TempFiles();
        var first = MediaReferences.InspectFile(temp.Add("old.wav"));
        var newPath = temp.Add("new.wav", [8, 7, 6, 5, 4]);
        var baseState = Timeline();
        var original = new Clip(Guid.NewGuid(), first.Id, MediaKind.Audio, 2, T(3), T(4),
            AssociationKind.Positioned, baseState.Clips[0].Id, T(3), EffectsToken: "gain:0.4");
        var state = TimelineEngine.Insert(baseState, baseState.Version, original).State;
        var session = new TimelineSession(state, [first]);
        session.RelinkMedia(first.Id, newPath);

        Assert.Equal(state.Clips, session.Current.Clips);
        Assert.Equal(state.MarkerItems, session.Current.MarkerItems);
        var updated = Assert.Single(session.Media);
        Assert.Equal(first.Id, updated.Id);
        Assert.Equal(newPath, updated.SourcePath);
        Assert.Equal(5, updated.SizeBytes);
        Assert.Equal("gain:0.4", session.Current.Find(original.Id).EffectsToken);
        var saved = ProjectStore.Deserialize(ProjectStore.Serialize(
            new EditorProject(Guid.NewGuid(), "Relink", session.Current, session.Media)));
        Assert.Equal(first.Id, Assert.Single(saved.MediaItems).Id);
        Assert.Equal(newPath, saved.MediaItems[0].SourcePath);
    }

    [Fact]
    public void MissingOriginalCanBeRelinkedAndWarningConditionResolves()
    {
        using var temp = new TempFiles();
        var oldPath = temp.Add("missing.png");
        var original = MediaReferences.InspectFile(oldPath);
        File.Delete(oldPath);
        Assert.False(MediaReferences.IsAvailable(original));
        var session = new TimelineSession(Timeline(), [original]);
        var replacement = temp.Add("restored.jpg");
        session.RelinkMedia(original.Id, replacement);
        Assert.True(MediaReferences.IsAvailable(Assert.Single(session.Media)));
        Assert.Equal(original.Id, session.Media[0].Id);
    }

    [Fact]
    public void RelinkRejectsTypeMismatchWithoutChangingHistory()
    {
        using var temp = new TempFiles();
        var original = MediaReferences.InspectFile(temp.Add("audio.mp3"));
        var invalid = temp.Add("picture.png");
        var state = Timeline();
        var session = new TimelineSession(state, [original]);
        Assert.Throws<MediaReferenceException>(() => session.RelinkMedia(original.Id, invalid));
        Assert.Same(state, session.Current);
        Assert.Equal(original, Assert.Single(session.Media));
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void RelinkRejectsDuplicatedSourceWithoutChangingCatalog()
    {
        using var temp = new TempFiles();
        var a = MediaReferences.InspectFile(temp.Add("a.wav"));
        var b = MediaReferences.InspectFile(temp.Add("b.wav"));
        var session = new TimelineSession(Timeline(), [a, b]);
        Assert.Throws<MediaReferenceException>(() => session.RelinkMedia(a.Id, b.SourcePath));
        Assert.Equal([a, b], session.Media);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void RelinkRejectsEmptyOrMissingSource()
    {
        using var temp = new TempFiles();
        var a = MediaReferences.InspectFile(temp.Add("a.wav"));
        var session = new TimelineSession(Timeline(), [a]);
        Assert.Throws<MediaReferenceException>(() =>
            session.RelinkMedia(a.Id, Path.Combine(temp.Folder, "missing.wav")));
        Assert.Throws<MediaReferenceException>(() => session.RelinkMedia(a.Id, temp.Add("empty.wav", [])));
        Assert.Equal(a, Assert.Single(session.Media));
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void UndoAndRedoRestoreMediaReferenceExactly()
    {
        using var temp = new TempFiles();
        var a = MediaReferences.InspectFile(temp.Add("one.mp4"));
        var b = temp.Add("two.mov");
        var session = new TimelineSession(Timeline(), [a]);
        session.RelinkMedia(a.Id, b);
        Assert.True(session.CanUndo);
        Assert.Equal(b, session.Media[0].SourcePath);

        session.Undo();
        Assert.Equal(a, Assert.Single(session.Media));
        Assert.Equal(a.SourcePath, session.Media[0].SourcePath);
        session.Redo();
        Assert.Equal(b, session.Media[0].SourcePath);
    }

    [Fact]
    public void TimelineAndCatalogShareOneChronologicalUndoHistory()
    {
        using var temp = new TempFiles();
        var asset = MediaReferences.InspectFile(temp.Add("before.mp3"));
        var newPath = temp.Add("after.m4a");
        var session = new TimelineSession(Timeline());
        var clip = MediaReferences.PlanClip(session.Current, asset, T(2), Guid.NewGuid());
        session.Execute(s => TimelineEngine.Insert(s, s.Version, clip), [asset]);
        session.RelinkMedia(asset.Id, newPath);
        var marker = new TimelineMarker(Guid.NewGuid(), T(4), "Edição");
        session.Execute(s => TimelineEngine.AddMarker(s, s.Version, marker));
        Assert.Single(session.Current.MarkerItems);

        session.Undo(); // marker
        Assert.Empty(session.Current.MarkerItems);
        Assert.Equal(newPath, session.Media[0].SourcePath);
        session.Undo(); // source relink
        Assert.Equal(asset.SourcePath, session.Media[0].SourcePath);
        Assert.Single(session.Current.Clips.Where(c => c.Id == clip.Id));
        session.Undo(); // original import
        Assert.Empty(session.Media);
        Assert.DoesNotContain(session.Current.Clips, c => c.Id == clip.Id);

        session.Redo();
        Assert.Equal(asset, Assert.Single(session.Media));
        session.Redo();
        Assert.Equal(newPath, session.Media[0].SourcePath);
        session.Redo();
        Assert.Single(session.Current.MarkerItems);
    }

    [Fact]
    public void NewEditAfterUndoInvalidatesRedoIncludingCatalog()
    {
        using var temp = new TempFiles();
        var asset = MediaReferences.InspectFile(temp.Add("one.wav"));
        var session = new TimelineSession(Timeline(), [asset]);
        session.RelinkMedia(asset.Id, temp.Add("two.wav"));
        session.Undo();
        Assert.True(session.CanRedo);

        session.Execute(s => TimelineEngine.AddMarker(s, s.Version,
            new TimelineMarker(Guid.NewGuid(), T(3), "Nova alteração")));
        Assert.False(session.CanRedo);
        Assert.Equal(asset, Assert.Single(session.Media));
    }

    [Fact]
    public void RelinkToIdenticalFileDoesNotCreateEmptyUndoEntry()
    {
        using var temp = new TempFiles();
        var asset = MediaReferences.InspectFile(temp.Add("unchanged.png"));
        var session = new TimelineSession(Timeline(), [asset]);
        session.RelinkMedia(asset.Id, asset.SourcePath);
        Assert.False(session.CanUndo);
        Assert.Equal(asset, Assert.Single(session.Media));
    }
}
