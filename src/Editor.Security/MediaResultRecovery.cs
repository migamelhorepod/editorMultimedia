using System.Text.Json;

namespace Editor.Security;

/// <summary>
/// Stages outputs outside the active media library and commits them atomically.
/// A crash can leave .partial files; recovery only deletes files owned by this
/// coordinator and never touches media originals or arbitrary user paths.
/// </summary>
public sealed class MediaResultRecovery
{
    private readonly string root;
    public MediaResultRecovery(string privateResultsDirectory)
    {
        if (string.IsNullOrWhiteSpace(privateResultsDirectory) ||
            !Path.IsPathFullyQualified(privateResultsDirectory))
            throw new ArgumentException("A private absolute result directory is required.", nameof(privateResultsDirectory));
        root = Path.GetFullPath(privateResultsDirectory);
        Directory.CreateDirectory(root);
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new UnauthorizedAccessException("Reparse-point recovery directories are prohibited.");
    }

    public string Begin(Guid taskId)
    {
        if (taskId == Guid.Empty) throw new ArgumentException("Missing task ID.", nameof(taskId));
        string partial = Path.Combine(root, taskId.ToString("N") + ".partial");
        using var file = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        return partial;
    }

    public string Commit(Guid taskId)
    {
        string src = Path.Combine(root, taskId.ToString("N") + ".partial");
        string dst = Path.Combine(root, taskId.ToString("N") + ".complete");
        if (!File.Exists(src) || File.Exists(dst))
            throw new InvalidOperationException("Invalid result transition.");
        File.Move(src, dst);
        return dst;
    }

    public RecoveryResult Recover()
    {
        int removed = 0, retained = 0;
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(file);
            if (name.EndsWith(".partial", StringComparison.Ordinal) &&
                Guid.TryParseExact(name[..^".partial".Length], "N", out _))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                    throw new UnauthorizedAccessException("Unsafe partial result reparse point.");
                File.Delete(file);
                removed++;
            }
            else if (name.EndsWith(".complete", StringComparison.Ordinal) &&
                Guid.TryParseExact(name[..^".complete".Length], "N", out _))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                    throw new UnauthorizedAccessException("Unsafe completed result reparse point.");
                using var stream = File.OpenRead(file);
                _ = stream.Length;
                retained++;
            }
        }
        return new RecoveryResult(removed, retained);
    }
}
public sealed record RecoveryResult(int IncompleteDiscarded, int CompletedPreserved);
