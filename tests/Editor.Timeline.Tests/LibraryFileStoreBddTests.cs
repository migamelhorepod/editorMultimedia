using Editor.Security;
using Xunit;

namespace Editor.Timeline.Tests;

public sealed class LibraryFileStoreBddTests
{
    [Fact]
    public async Task GivenExternalMedia_WhenImported_ThenStoredOnLibraryAndOriginalPreserved()
    {
        string home = Path.Combine(Path.GetTempPath(), "editor-lib-" + Guid.NewGuid().ToString("N"));
        string library = Path.Combine(home, "library");
        string outside = Path.Combine(home, "outside");
        Directory.CreateDirectory(library);
        Directory.CreateDirectory(outside);
        try
        {
            var source = Path.Combine(outside, "source.wav");
            var bytes = Enumerable.Range(0, 2048).Select(x => (byte)x).ToArray();
            await File.WriteAllBytesAsync(source, bytes);
            var store = new LibraryFileStore(library);
            var imported = await store.ImportAsync(source);
            Assert.True(imported.NewlyCopied);
            Assert.Equal(Path.Combine(library, "source.wav"), imported.Path);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(source));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(imported.Path));
            Assert.Empty(Directory.GetFiles(library, "*.partial"));
        }
        finally { Directory.Delete(home, true); }
    }

    [Fact]
    public async Task GivenDuplicateContents_WhenImportedAgain_ThenReusedWithoutSecondPhysicalCopy()
    {
        string home = Path.Combine(Path.GetTempPath(), "editor-lib-" + Guid.NewGuid().ToString("N"));
        string library = Path.Combine(home, "library");
        Directory.CreateDirectory(library);
        try
        {
            var a = Path.Combine(home, "first.wav");
            var b = Path.Combine(home, "second.wav");
            await File.WriteAllBytesAsync(a, [1, 2, 3, 4, 5]);
            await File.WriteAllBytesAsync(b, [1, 2, 3, 4, 5]);
            var store = new LibraryFileStore(library);
            var first = await store.ImportAsync(a);
            var repeated = await store.ImportAsync(b);
            Assert.True(repeated.Duplicate);
            Assert.False(repeated.NewlyCopied);
            Assert.Equal(first.Path, repeated.Path);
            Assert.Single(Directory.GetFiles(library));
        }
        finally { Directory.Delete(home, true); }
    }

    [Fact]
    public async Task GivenSameNameDifferentBytes_WhenImported_ThenFilesGetDistinctNames()
    {
        string home = Path.Combine(Path.GetTempPath(), "editor-lib-" + Guid.NewGuid().ToString("N"));
        string library = Path.Combine(home, "library");
        string outside = Path.Combine(home, "outside");
        Directory.CreateDirectory(library);
        Directory.CreateDirectory(outside);
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(library, "same.wav"), [1, 2]);
            var source = Path.Combine(outside, "same.wav");
            await File.WriteAllBytesAsync(source, [3, 4]);
            var imported = await new LibraryFileStore(library).ImportAsync(source);
            Assert.True(imported.NewlyCopied);
            Assert.Equal("same (1).wav", Path.GetFileName(imported.Path));
            Assert.Equal(new byte[] { 1, 2 }, await File.ReadAllBytesAsync(Path.Combine(library, "same.wav")));
            Assert.Equal(new byte[] { 3, 4 }, await File.ReadAllBytesAsync(source));
        }
        finally { Directory.Delete(home, true); }
    }

    [Fact]
    public async Task GivenStorageMissing_WhenImportRequested_ThenRejectWithoutTouchingSource()
    {
        string home = Path.Combine(Path.GetTempPath(), "editor-lib-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(home);
        try
        {
            var source = Path.Combine(home, "valid.wav");
            await File.WriteAllBytesAsync(source, [1, 2, 3]);
            await Assert.ThrowsAsync<IOException>(() =>
                new LibraryFileStore(Path.Combine(home, "missing")).ImportAsync(source));
            Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(source));
        }
        finally { Directory.Delete(home, true); }
    }
}
