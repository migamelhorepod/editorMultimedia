namespace Editor.Security;

/// <summary>
/// Rejects implicit use of an entire volume or Windows-managed folders as
/// a media library. Library scans must only inspect a user-selected,
/// ordinary directory within the approved repository drive D:.
/// </summary>
public static class LibraryRootPolicy
{
    public static string ValidateUserSelected(string selectedFolder)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Biblioteca local requer Windows.");
        if (string.IsNullOrWhiteSpace(selectedFolder) ||
            !Path.IsPathFullyQualified(selectedFolder))
            throw new ArgumentException("Selecione uma pasta absoluta dentro do disco D:.");

        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(selectedFolder));
        string drive = Path.GetPathRoot(full) ?? "";
        if (!string.Equals(drive, @"D:\", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("A biblioteca deve estar dentro do disco D:.");

        string driveRoot = Path.TrimEndingDirectorySeparator(drive);
        if (string.Equals(full, driveRoot, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException(
                "A raiz D: nao pode ser usada como biblioteca. Selecione uma pasta especifica.");

        string relative = Path.GetRelativePath(drive, full);
        string firstSegment = relative.Split(Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar)[0];
        if (firstSegment.Equals("WindowsApps", StringComparison.OrdinalIgnoreCase) ||
            firstSegment.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase) ||
            firstSegment.Equals("$RECYCLE.BIN", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException(
                "Nao selecione pastas administradas pelo Windows como biblioteca.");

        if (!Directory.Exists(full))
            throw new DirectoryNotFoundException(
                "Pasta da biblioteca nao encontrada. Crie-a primeiro no Explorador.");
        // Do not follow symlinks/junctions into Windows-managed or external
        // paths. Check ancestors between the requested folder and the drive.
        string cursor = full;
        while (!string.Equals(cursor, driveRoot, StringComparison.OrdinalIgnoreCase))
        {
            if ((File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException(
                    "Pasta da biblioteca nao pode conter links ou junctions.");
            cursor = Path.GetDirectoryName(cursor)
                ?? throw new UnauthorizedAccessException("Caminho de biblioteca invalido.");
        }
        return full;
    }
}
