using System.Security.Cryptography;

namespace Editor.Security;

/// <summary>
/// Crash-resilient staging for private, application-owned results only.
/// An incomplete result is never exposed to projects or libraries.
/// </summary>
public sealed class MediaResultRecovery
{
    private readonly string root;

    public MediaResultRecovery(string privateResultsDirectory)
    {
        if (string.IsNullOrWhiteSpace(privateResultsDirectory) ||
            !Path.IsPathFullyQualified(privateResultsDirectory))
            throw new ArgumentException("Private absolute result directory required.", nameof(privateResultsDirectory));
        root = Path.GetFullPath(privateResultsDirectory);
        Directory.CreateDirectory(root);
        if (IsLink(root)) throw new UnauthorizedAccessException("Redirected results directory.");
    }

    public string Begin(Guid taskId)
    {
        ValidateId(taskId);
        var path = ResultPath(taskId, ".partial");
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        return path;
    }

    public string Commit(Guid taskId)
    {
        ValidateId(taskId);
        string partial = ResultPath(taskId, ".partial");
        string complete = ResultPath(taskId, ".complete");
        string pending = ResultPath(taskId, ".sha256.pending");
        string manifest = ResultPath(taskId, ".sha256");
        if (!File.Exists(partial) || File.Exists(complete) ||
            File.Exists(pending) || File.Exists(manifest) || IsLink(partial))
            throw new InvalidOperationException("Invalid result state.");
        string hash = ComputeHash(partial);
        using (var file = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(file))
        {
            writer.Write(hash);
            writer.Flush();
            file.Flush(flushToDisk: true);
        }
        File.Move(partial, complete);
        File.Move(pending, manifest);
        return complete;
    }

    public RecoveryResult Recover()
    {
        int discarded = 0;
        int retained = 0;
        var entries = Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly).ToArray();
        foreach (var path in entries)
        {
            string name = Path.GetFileName(path);
            if (TryTaskFile(name, ".partial", out _) ||
                TryTaskFile(name, ".sha256.pending", out _))
            {
                SafeDelete(path);
                discarded++;
            }
        }
        foreach (var path in Directory.EnumerateFiles(root, "*.complete", SearchOption.TopDirectoryOnly))
        {
            if (!TryTaskFile(Path.GetFileName(path), ".complete", out Guid id)) continue;
            string hashFile = ResultPath(id, ".sha256");
            bool verified = File.Exists(hashFile) && !IsLink(path) && !IsLink(hashFile) &&
                string.Equals(File.ReadAllText(hashFile).Trim(), ComputeHash(path),
                    StringComparison.OrdinalIgnoreCase);
            if (verified) retained++;
            else
            {
                SafeDelete(path);
                if (File.Exists(hashFile)) SafeDelete(hashFile);
                discarded++;
            }
        }
        // Orphan manifests cannot validate any usable result.
        foreach (var path in Directory.EnumerateFiles(root, "*.sha256", SearchOption.TopDirectoryOnly))
        {
            if (TryTaskFile(Path.GetFileName(path), ".sha256", out Guid id) &&
                !File.Exists(ResultPath(id, ".complete")))
                SafeDelete(path);
        }
        return new RecoveryResult(discarded, retained);
    }

    private string ResultPath(Guid id, string suffix) =>
        Path.Combine(root, id.ToString("N") + suffix);

    private static void ValidateId(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("Task identity required.");
    }

    private static bool TryTaskFile(string name, string suffix, out Guid id)
    {
        id = Guid.Empty;
        return name.EndsWith(suffix, StringComparison.Ordinal) &&
            Guid.TryParseExact(name.AsSpan(0, name.Length - suffix.Length), "N", out id);
    }

    private static bool IsLink(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static void SafeDelete(string path)
    {
        if (IsLink(path)) throw new UnauthorizedAccessException("Unsafe recovery reparse point.");
        File.Delete(path);
    }

    private static string ComputeHash(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(file));
    }
}
public sealed record RecoveryResult(int IncompleteDiscarded, int CompletedPreserved);
