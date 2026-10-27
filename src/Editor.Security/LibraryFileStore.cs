using System.Buffers;

namespace Editor.Security;

/// <summary>
/// Copies user-selected external files into the active library folder.
/// It does not decode or process media, and never grants production worker access.
/// New bytes are staged in the destination directory and promoted only when complete.
/// </summary>
public sealed class LibraryFileStore
{
    public sealed record StoredMedia(string Path, bool NewlyCopied, bool Duplicate);

    private readonly string root;
    public string Root => root;

    public LibraryFileStore(string libraryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryRoot);
        if (!Path.IsPathFullyQualified(libraryRoot))
            throw new ArgumentException("Library root must be absolute.", nameof(libraryRoot));
        root = Path.GetFullPath(libraryRoot);
    }

    public async Task<StoredMedia> ImportAsync(string externalFile, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalFile);
        if (!Path.IsPathFullyQualified(externalFile))
            throw new ArgumentException("Media source must be absolute.", nameof(externalFile));
        var source = Path.GetFullPath(externalFile);
        AssertAvailableRoot();
        if (!File.Exists(source))
            throw new FileNotFoundException("A mídia selecionada não está disponível.", source);
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
            throw new UnauthorizedAccessException("Mídias em links simbólicos não são aceitas.");
        if (IsWithinRoot(source))
            return new StoredMedia(source, false, false);

        string name = Path.GetFileName(source);
        if (string.IsNullOrEmpty(name) || name is "." or "..")
            throw new IOException("Nome de mídia inválido.");
        string temp = Path.Combine(root, ".media-import-" + Guid.NewGuid().ToString("N") + ".partial");
        try
        {
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read,
                FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await input.CopyToAsync(output, 1024 * 1024, token);
                await output.FlushAsync(token);
            }

            AssertAvailableRoot();
            var stagedLength = new FileInfo(temp).Length;
            foreach (var candidate in Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly))
            {
                token.ThrowIfCancellationRequested();
                if (string.Equals(candidate, temp, StringComparison.OrdinalIgnoreCase)) continue;
                var attributes = File.GetAttributes(candidate);
                if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                if (Path.GetFileName(candidate).StartsWith(".media-import-", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (new FileInfo(candidate).Length != stagedLength) continue;
                if (await EqualContentsAsync(temp, candidate, token))
                    return new StoredMedia(candidate, false, true);
            }

            var basename = Path.GetFileNameWithoutExtension(name);
            var extension = Path.GetExtension(name);
            for (var suffix = 0; suffix < 10000; suffix++)
            {
                var dest = Path.Combine(root, suffix == 0 ? name : basename + " (" + suffix + ")" + extension);
                if (File.Exists(dest)) continue;
                File.Move(temp, dest, false);
                return new StoredMedia(dest, true, false);
            }
            throw new IOException("Não há nome disponível para importar a mídia.");
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch (IOException) { /* Diagnosed by startup maintenance; original stays intact. */ }
            catch (UnauthorizedAccessException) { /* Do not mask initial import failure. */ }
        }
    }

    private bool IsWithinRoot(string path)
    {
        string prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private void AssertAvailableRoot()
    {
        if (!Directory.Exists(root))
            throw new IOException("A biblioteca no disco D: está indisponível.");
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new UnauthorizedAccessException("A pasta da biblioteca não pode ser um link simbólico.");
    }

    private static async Task<bool> EqualContentsAsync(string left, string right, CancellationToken token)
    {
        await using var a = new FileStream(left, FileMode.Open, FileAccess.Read,
            FileShare.Read, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var b = new FileStream(right, FileMode.Open, FileAccess.Read,
            FileShare.Read, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (a.Length != b.Length) return false;
        byte[] one = ArrayPool<byte>.Shared.Rent(131072);
        byte[] two = ArrayPool<byte>.Shared.Rent(131072);
        try
        {
            int count;
            while ((count = await a.ReadAsync(one, token)) > 0)
            {
                int read = 0;
                while (read < count)
                {
                    int next = await b.ReadAsync(two.AsMemory(read, count - read), token);
                    if (next == 0) return false;
                    read += next;
                }
                if (!one.AsSpan(0, count).SequenceEqual(two.AsSpan(0, count))) return false;
            }
            return true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(one);
            ArrayPool<byte>.Shared.Return(two);
        }
    }
}
