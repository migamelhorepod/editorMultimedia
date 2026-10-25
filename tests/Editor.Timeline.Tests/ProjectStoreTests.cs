using System.Text;
using Editor.Timeline;
using Xunit;

namespace Editor.Timeline.Tests;

public sealed class ProjectStoreTests
{
    private static MediaTime T(decimal s) => MediaTime.FromSeconds(s);

    private static EditorProject Sample()
    {
        var a = new Clip(Guid.NewGuid(), Guid.NewGuid(), MediaKind.Video, 0, T(0), T(8));
        var b = new Clip(Guid.NewGuid(), Guid.NewGuid(), MediaKind.Video, 0, T(8), T(9));
        var sec = new Clip(Guid.NewGuid(), Guid.NewGuid(), MediaKind.Audio, 3, T(2.25m), T(4.75m),
            AssociationKind.Positioned, a.Id, T(2.25m), EffectsToken: "gain:0.8");
        var orphan = new Clip(Guid.NewGuid(), Guid.NewGuid(), MediaKind.Image, 5, T(10), T(2),
            OrphanNotice: true);
        var state = new TimelineState(7, [a, b, sec, orphan],
            [new TimelineMarker(Guid.NewGuid(), T(2.5m), "Intro"),
             new TimelineMarker(Guid.NewGuid(), T(12), "Ponto final")]);
        return new EditorProject(Guid.NewGuid(), "Teste de projeto", state);
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Folder { get; } = Path.Combine(Path.GetTempPath(), $"editor-v60-{Guid.NewGuid():N}");
        public TempDirectory() => Directory.CreateDirectory(Folder);
        public string File(string name = "teste.emproj.json") => Path.Combine(Folder, name);
        public void Dispose() => Directory.Delete(Folder, recursive: true);
    }

    [Fact] public void RoundTripRetainsProjectIdentityAndTimelineVersion()
    {
        var original = Sample();
        var loaded = ProjectStore.Deserialize(ProjectStore.Serialize(original));
        Assert.Equal(original.Id, loaded.Id);
        Assert.Equal(original.Name, loaded.Name);
        Assert.Equal(original.Timeline.Version, loaded.Timeline.Version);
        Assert.True(ProjectStore.SameContent(original.Timeline, loaded.Timeline));
    }

    [Fact] public void RoundTripRetainsExactFractionalTicksAndEffects()
    {
        var original = Sample();
        var loaded = ProjectStore.Deserialize(ProjectStore.Serialize(original));
        var audio = Assert.Single(loaded.Timeline.Clips, c => c.Kind == MediaKind.Audio);
        Assert.Equal(T(2.25m), audio.Start);
        Assert.Equal(T(4.75m), audio.Duration);
        Assert.Equal(T(2.25m), audio.ParentOffset);
        Assert.Equal("gain:0.8", audio.EffectsToken);
        Assert.Equal(original.Timeline.Clips[0].Id, audio.ParentId);
    }

    [Fact] public void RoundTripRetainsOrphanWarningAndMarkers()
    {
        var loaded = ProjectStore.Deserialize(ProjectStore.Serialize(Sample()));
        Assert.True(Assert.Single(loaded.Timeline.Clips, c => c.Kind == MediaKind.Image).OrphanNotice);
        Assert.Equal(new[] { "Intro", "Ponto final" }, loaded.Timeline.MarkerItems.Select(m => m.Label));
    }

    [Fact] public void WriteAndOpenProjectFile()
    {
        using var tmp = new TempDirectory();
        var original = Sample();
        ProjectStore.Save(tmp.File(), original);
        var loaded = ProjectStore.Load(tmp.File());
        Assert.Equal(original.Id, loaded.Id);
        Assert.True(ProjectStore.SameContent(original.Timeline, loaded.Timeline));
    }

    [Fact] public void SavingAgainReplacesExistingFileWithLatestState()
    {
        using var tmp = new TempDirectory();
        var initial = Sample();
        ProjectStore.Save(tmp.File(), initial);
        var updatedState = TimelineEngine.AddMarker(initial.Timeline, initial.Timeline.Version,
            new TimelineMarker(Guid.NewGuid(), T(19), "Outro ponto")).State;
        ProjectStore.Save(tmp.File(), initial with { Timeline = updatedState });
        var loaded = ProjectStore.Load(tmp.File());
        Assert.Equal(3, loaded.Timeline.MarkerItems.Count);
        Assert.Empty(Directory.GetFiles(tmp.Folder, "*.tmp"));
    }

    [Fact] public void InvalidProjectNeverTruncatesPreviouslySavedFile()
    {
        using var tmp = new TempDirectory();
        var valid = Sample();
        ProjectStore.Save(tmp.File(), valid);
        var before = System.IO.File.ReadAllBytes(tmp.File());
        var corrupted = valid with { Id = Guid.Empty };
        Assert.Throws<ProjectFileException>(() => ProjectStore.Save(tmp.File(), corrupted));
        Assert.Equal(before, System.IO.File.ReadAllBytes(tmp.File()));
    }

    [Fact] public void FilesAreUtf8WithoutBom()
    {
        using var tmp = new TempDirectory();
        ProjectStore.Save(tmp.File(), Sample());
        var bytes = System.IO.File.ReadAllBytes(tmp.File());
        Assert.True(bytes.Length >= 3);
        Assert.False(bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
    }

    [Fact] public void MissingProjectFileDoesNotCreateAnything()
    {
        using var tmp = new TempDirectory();
        Assert.Throws<FileNotFoundException>(() => ProjectStore.Load(tmp.File()));
        Assert.Empty(Directory.EnumerateFiles(tmp.Folder));
    }

    [Fact] public void UnavailableDestinationDoesNotCreateNewFolder()
    {
        using var tmp = new TempDirectory();
        var missing = Path.Combine(tmp.Folder, "nao-existe");
        Assert.Throws<DirectoryNotFoundException>(() => ProjectStore.Save(Path.Combine(missing, "p.emproj.json"), Sample()));
        Assert.False(Directory.Exists(missing));
    }

    [Fact] public void MalformedJsonIsRejected()
    {
        Assert.Throws<ProjectFileException>(() => ProjectStore.Deserialize("{ invalid"));
    }

    [Fact] public void EmptyObjectIsRejected()
    {
        Assert.Throws<ProjectFileException>(() => ProjectStore.Deserialize("{}"));
    }

    [Fact] public void FutureSchemaVersionIsRejected()
    {
        var json = ProjectStore.Serialize(Sample()).Replace("\"formatVersion\": 2", "\"formatVersion\": 3");
        Assert.Contains("Formato 3", Assert.Throws<ProjectFileException>(() => ProjectStore.Deserialize(json)).Message);
    }

    [Fact] public void UnknownPropertiesAreRejected()
    {
        var json = ProjectStore.Serialize(Sample()).Replace("\"formatVersion\": 2,", "\"formatVersion\": 2, \"unexpected\": true,");
        Assert.Throws<ProjectFileException>(() => ProjectStore.Deserialize(json));
    }

    [Fact] public void UnsupportedMediaKindIsRejected()
    {
        var json = ProjectStore.Serialize(Sample()).Replace("\"kind\": \"Audio\"", "\"kind\": \"Unknown\"");
        Assert.Throws<ProjectFileException>(() => ProjectStore.Deserialize(json));
    }

    [Fact] public void InvalidParentAssociationIsRejected()
    {
        var original = Sample();
        var changed = original.Timeline.Clips.Select(c => c.Association == AssociationKind.Positioned
            ? c with { ParentId = Guid.NewGuid() } : c).ToArray();
        var corrupt = original with { Timeline = original.Timeline with { Clips = changed } };
        Assert.Throws<ProjectFileException>(() => ProjectStore.Serialize(corrupt));
    }

    [Fact] public void DuplicateMarkerAtSameTimeIsRejected()
    {
        var original = Sample();
        var a = original.Timeline.MarkerItems[0];
        var b = original.Timeline.MarkerItems[1] with { At = a.At };
        var corrupt = original with { Timeline = original.Timeline with { Markers = [a, b] } };
        Assert.Throws<ProjectFileException>(() => ProjectStore.Serialize(corrupt));
    }

    [Fact] public void DuplicateClipIdsAreRejected()
    {
        var original = Sample();
        var clips = original.Timeline.Clips.ToArray();
        clips[1] = clips[1] with { Id = clips[0].Id };
        var invalid = original with { Timeline = original.Timeline with { Clips = clips } };
        Assert.Throws<ProjectFileException>(() => ProjectStore.Serialize(invalid));
    }

    [Fact] public void EmptyProjectIdAndInvalidNameAreRejected()
    {
        var original = Sample();
        Assert.Throws<ProjectFileException>(() => ProjectStore.Serialize(original with { Id = Guid.Empty }));
        Assert.Throws<ProjectFileException>(() => ProjectStore.Serialize(original with { Name = " " }));
        Assert.Throws<ProjectFileException>(() => ProjectStore.Serialize(original with { Name = "bad\nname" }));
    }

    [Fact] public void FilesOverSizeLimitCannotBeOpened()
    {
        using var tmp = new TempDirectory();
        using (var fs = new FileStream(tmp.File(), FileMode.CreateNew, FileAccess.Write))
            fs.SetLength(ProjectStore.MaxFileBytes + 1L);
        Assert.Throws<ProjectFileException>(() => ProjectStore.Load(tmp.File()));
    }

    [Fact] public void ProjectContentComparisonIgnoresHistoryVersion()
    {
        var a = Sample().Timeline;
        var b = a with { Version = a.Version + 100 };
        Assert.True(ProjectStore.SameContent(a, b));
    }

    [Fact] public void ProjectContentComparisonDetectsMarkerAndClipEdits()
    {
        var a = Sample().Timeline;
        var markerChanged = a with { Markers = a.MarkerItems.Select(m => m with { Label = m.Label + "!" }).ToArray() };
        Assert.False(ProjectStore.SameContent(a, markerChanged));
        var clips = a.Clips.ToArray();
        clips[1] = clips[1] with { Duration = T(7) };
        Assert.False(ProjectStore.SameContent(a, a with { Clips = clips }));
    }

    [Fact] public void UndoBackToSavedContentIsCleanWithoutAutoSave()
    {
        var original = Sample().Timeline;
        var session = new TimelineSession(original);
        var marker = new TimelineMarker(Guid.NewGuid(), T(20), "Desfazer");
        session.Execute(s => TimelineEngine.AddMarker(s, s.Version, marker));
        Assert.False(ProjectStore.SameContent(original, session.Current));
        session.Undo();
        Assert.True(ProjectStore.SameContent(original, session.Current));
    }

    [Fact] public void NoPhysicalMediaFileIsSerialized()
    {
        var json = ProjectStore.Serialize(Sample());
        Assert.False(json.Contains("filePath", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("mediaId", json);
    }
}
