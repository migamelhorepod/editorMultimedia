using System.Text.Json.Nodes;
using Editor.Timeline;
using Xunit;

namespace Editor.Timeline.Tests;

public sealed class MediaReferencesTests
{
    private static MediaTime T(decimal seconds) => MediaTime.FromSeconds(seconds);

    private sealed class TempMedia : IDisposable
    {
        public string Folder { get; } = Path.Combine(Path.GetTempPath(), $"editor-media-{Guid.NewGuid():N}");
        public TempMedia() => Directory.CreateDirectory(Folder);
        public string Create(string name)
        {
            var file = Path.Combine(Folder, name);
            File.WriteAllBytes(file, [1, 2, 3, 4]);
            return file;
        }
        public void Dispose() => Directory.Delete(Folder, recursive: true);
    }

    private static TimelineState Demo()
    {
        var main = new Clip(Guid.NewGuid(), Guid.NewGuid(), MediaKind.Video, 0, T(0), T(8));
        return new TimelineState(0, [main]);
    }

    [Theory]
    [InlineData("camera.MP4", MediaKind.Video)]
    [InlineData("narracao.WAV", MediaKind.Audio)]
    [InlineData("capa.png", MediaKind.Image)]
    public void DetectsTypeAndDoesNotModifySource(string name, MediaKind expected)
    {
        using var tmp = new TempMedia();
        var file = tmp.Create(name);
        var bytes = File.ReadAllBytes(file);
        var asset = MediaReferences.InspectFile(file);
        Assert.Equal(expected, asset.Kind);
        Assert.Equal(name, asset.DisplayName);
        Assert.Equal(Path.GetFullPath(file), asset.SourcePath);
        Assert.Equal(4, asset.SizeBytes);
        Assert.Equal(bytes, File.ReadAllBytes(file));
    }

    [Fact] public void RejectsUnsupportedExtension()
    {
        using var tmp = new TempMedia();
        Assert.Throws<MediaReferenceException>(() => MediaReferences.InspectFile(tmp.Create("foo.exe")));
    }

    [Fact] public void RejectsMissingOrEmptyFile()
    {
        using var tmp = new TempMedia();
        var empty = Path.Combine(tmp.Folder, "empty.wav");
        File.WriteAllBytes(empty, []);
        Assert.Throws<MediaReferenceException>(() => MediaReferences.InspectFile(empty));
        Assert.Throws<MediaReferenceException>(() => MediaReferences.InspectFile(Path.Combine(tmp.Folder, "missing.mp3")));
    }

    [Fact] public void SecondaryImportFindsFreeTrackAndAssociatesAutomatically()
    {
        using var tmp = new TempMedia();
        var asset = MediaReferences.InspectFile(tmp.Create("audio.wav"));
        var state = Demo();
        var existing = new Clip(Guid.NewGuid(), Guid.NewGuid(), MediaKind.Image, 1, T(2), T(10));
        state = TimelineEngine.Insert(state, state.Version, existing).State;
        var planned = MediaReferences.PlanClip(state, asset, T(3), Guid.NewGuid());
        Assert.Equal(2, planned.Track);
        Assert.Equal(T(3), planned.Start);
        Assert.Equal(T(5), planned.Duration);
        var next = TimelineEngine.Insert(state, state.Version, planned).State;
        Assert.Equal(AssociationKind.Positioned, next.Find(planned.Id).Association);
        Assert.Equal(state.Clips[0].Id, next.Find(planned.Id).ParentId);
    }

    [Fact] public void VideoImportAppendsToMainTrack()
    {
        using var tmp = new TempMedia();
        var asset = MediaReferences.InspectFile(tmp.Create("camera.mov")) with { DurationTicks = T(5).Ticks };
        var state = Demo();
        var planned = MediaReferences.PlanClip(state, asset, T(2), Guid.NewGuid());
        Assert.Equal(0, planned.Track);
        Assert.Equal(T(8), planned.Start);
        Assert.Equal(T(5), planned.Duration);
        TimelineEngine.Validate(TimelineEngine.Insert(state, state.Version, planned).State);
    }

    [Fact] public void FullSecondaryTracksRejectInsertionWithoutModifyingInput()
    {
        using var tmp = new TempMedia();
        var asset = MediaReferences.InspectFile(tmp.Create("audio.mp3"));
        var state = Demo();
        for (var lane = 1; lane < TimelineEngine.MaxTracks; lane++)
        {
            var clip = new Clip(Guid.NewGuid(), Guid.NewGuid(), MediaKind.Audio, lane, T(0), T(5));
            state = TimelineEngine.Insert(state, state.Version, clip).State;
        }
        var snapshot = state;
        var error = Assert.Throws<TimelineException>(
            () => MediaReferences.PlanClip(snapshot, asset, T(0), Guid.NewGuid()));
        Assert.Equal("NO_FREE_TRACK", error.Code);
        Assert.Same(snapshot, state);
    }

    [Fact] public void FormatV2RoundTripKeepsMediaReferences()
    {
        using var tmp = new TempMedia();
        var asset = MediaReferences.InspectFile(tmp.Create("audio.mp3"));
        var state = Demo();
        var clip = MediaReferences.PlanClip(state, asset, T(0), Guid.NewGuid());
        state = TimelineEngine.Insert(state, state.Version, clip).State;
        var project = new EditorProject(Guid.NewGuid(), "Importado", state, [asset]);
        var json = ProjectStore.Serialize(project);
        Assert.Contains("\"formatVersion\": 3", json);
        var loaded = ProjectStore.Deserialize(json);
        Assert.Equal(asset, Assert.Single(loaded.MediaItems));
        Assert.True(ProjectStore.SameContent(project, loaded));
    }

    [Fact] public void FormatV1StillLoadsWithEmptyMediaCatalog()
    {
        var original = new EditorProject(Guid.NewGuid(), "Antigo", Demo());
        var node = JsonNode.Parse(ProjectStore.Serialize(original))!.AsObject();
        node["formatVersion"] = 1;
        node.Remove("media");
        var loaded = ProjectStore.Deserialize(node.ToJsonString());
        Assert.Empty(loaded.MediaItems);
        Assert.True(ProjectStore.SameContent(original, loaded));
    }

    [Fact] public void MissingSourcePreservesReferenceAndProject()
    {
        MediaAsset asset;
        EditorProject original;
        using (var tmp = new TempMedia())
        {
            asset = MediaReferences.InspectFile(tmp.Create("missing.wav"));
            original = new EditorProject(Guid.NewGuid(), "Ausente", Demo(), [asset]);
        }
        Assert.False(MediaReferences.IsAvailable(asset));
        var loaded = ProjectStore.Deserialize(ProjectStore.Serialize(original));
        Assert.Equal(asset, Assert.Single(loaded.MediaItems));
        Assert.True(ProjectStore.SameContent(original, loaded));
    }

    [Fact] public void WrongClipTypeCannotBeSerialized()
    {
        using var tmp = new TempMedia();
        var asset = MediaReferences.InspectFile(tmp.Create("image.png"));
        var project = new EditorProject(Guid.NewGuid(), "Invalido",
            new TimelineState(0, [new Clip(Guid.NewGuid(), asset.Id, MediaKind.Audio, 1, T(0), T(4))]), [asset]);
        Assert.Throws<ProjectFileException>(() => ProjectStore.Serialize(project));
    }

    [Fact] public void DuplicateSourcePathsAreRejectedIgnoringCase()
    {
        using var tmp = new TempMedia();
        var asset = MediaReferences.InspectFile(tmp.Create("image.png"));
        var same = asset with { Id = Guid.NewGuid() };
        var project = new EditorProject(Guid.NewGuid(), "Duplicado", Demo(), [asset, same]);
        Assert.Throws<ProjectFileException>(() => ProjectStore.Serialize(project));
    }
}
