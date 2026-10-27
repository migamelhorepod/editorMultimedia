using Editor.Timeline;
using Xunit;

namespace Editor.Timeline.Tests;

public sealed class CatalogDeleteBddTests
{
    private static MediaAsset Asset()
    {
        var path = Path.Combine(Path.GetTempPath(), "catalog-delete-" + Guid.NewGuid().ToString("N") + ".png");
        // Metadata is intentionally a project reference; deleting catalog entries
        // must not access or remove source files.
        return new MediaAsset(Guid.NewGuid(), MediaKind.Image, Path.GetFileName(path), path, 0);
    }

    [Fact]
    public void GivenUnusedCatalogMedia_WhenDeleted_ThenOnlyCatalogReferenceDisappears()
    {
        var asset = Asset();
        var session = new TimelineSession(new TimelineState(0, Array.Empty<Clip>()), [asset]);
        session.DeleteCatalogMedia(asset.Id);
        Assert.Empty(session.Media);
        Assert.Empty(session.Current.Clips);
        session.Undo();
        Assert.Single(session.Media);
    }

    [Fact]
    public void GivenCatalogMediaUsedTwice_WhenDeleted_ThenEveryTimelineOccurrenceIsRemovedInOneUndo()
    {
        var asset = Asset();
        var first = new Clip(Guid.NewGuid(), asset.Id, MediaKind.Image, 0,
            MediaTime.Zero, MediaTime.FromSeconds(5));
        var second = new Clip(Guid.NewGuid(), asset.Id, MediaKind.Image, 0,
            MediaTime.FromSeconds(5), MediaTime.FromSeconds(5));
        var session = new TimelineSession(new TimelineState(0, [first, second]), [asset]);
        session.DeleteCatalogMedia(asset.Id);
        Assert.Empty(session.Media);
        Assert.Empty(session.Current.Clips);
        session.Undo();
        Assert.Single(session.Media);
        Assert.Equal(2, session.Current.Clips.Count);
        session.Redo();
        Assert.Empty(session.Media);
    }
}
