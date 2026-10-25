using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Editor.Timeline;

/// <summary>A project stores the timeline and user-approved media references only.
/// Source media bytes are never embedded in a project file.</summary>
public sealed record EditorProject(Guid Id, string Name, TimelineState Timeline,
    IReadOnlyList<MediaAsset>? Media = null)
{
    public IReadOnlyList<MediaAsset> MediaItems => Media ?? Array.Empty<MediaAsset>();
}

public sealed class ProjectFileException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Versioned JSON project format, validated on load; atomic single-file save.
/// In the production architecture this I/O will move behind IFileAccessService.
/// The prototype deliberately never writes a project unless Save is called.
/// </summary>
public static class ProjectStore
{
    public const int FormatVersion = 2;
    public const int MaxFileBytes = 16 * 1024 * 1024;
    private const int MaxClips = 50_000;
    private const int MaxMarkers = 20_000;
    private const int MaxMedia = 50_000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public static bool SameContent(TimelineState a, TimelineState b) =>
        a.Clips.SequenceEqual(b.Clips) && a.MarkerItems.SequenceEqual(b.MarkerItems);

    public static bool SameContent(EditorProject a, EditorProject b) =>
        a.Id == b.Id && a.Name == b.Name && SameContent(a.Timeline, b.Timeline) &&
        a.MediaItems.SequenceEqual(b.MediaItems);

    private static void ValidateProject(EditorProject project)
    {
        if (project.Id == Guid.Empty || string.IsNullOrWhiteSpace(project.Name) ||
            project.Name.Length > 120 || project.Name.Any(char.IsControl))
            throw new ProjectFileException("Identificador ou nome do projeto inválido.");
        if (project.Timeline is null || project.Timeline.Clips is null ||
            project.Timeline.Clips.Count > MaxClips || project.Timeline.MarkerItems.Count > MaxMarkers)
            throw new ProjectFileException("Quantidade de mídias ou marcadores inválida.");
        if (project.MediaItems.Count > MaxMedia)
            throw new ProjectFileException("Catálogo de mídias maior que o limite permitido.");
        try
        {
            TimelineEngine.Validate(project.Timeline);
            MediaReferences.ValidateCatalog(project.MediaItems, project.Timeline);
        }
        catch (TimelineException ex) { throw new ProjectFileException($"Timeline inválida: {ex.Message}", ex); }
        catch (MediaReferenceException ex) { throw new ProjectFileException($"Catálogo de mídias inválido: {ex.Message}", ex); }
    }

    public static string Serialize(EditorProject project)
    {
        ValidateProject(project);
        var file = new ProjectFile
        {
            FormatVersion = FormatVersion,
            ProjectId = project.Id,
            Name = project.Name,
            TimelineVersion = project.Timeline.Version,
            Clips = project.Timeline.Clips.Select(c => new ClipFile
            {
                Id = c.Id, MediaId = c.MediaId, Kind = c.Kind, Track = c.Track,
                StartTicks = c.Start.Ticks, DurationTicks = c.Duration.Ticks,
                Association = c.Association, ParentId = c.ParentId,
                ParentOffsetTicks = c.ParentOffset.Ticks, OrphanNotice = c.OrphanNotice,
                EffectsToken = c.EffectsToken
            }).ToList(),
            Markers = project.Timeline.MarkerItems.Select(m => new MarkerFile
            {
                Id = m.Id, AtTicks = m.At.Ticks, Label = m.Label
            }).ToList(),
            Media = project.MediaItems.Select(m => new MediaFile
            {
                Id = m.Id, Kind = m.Kind, DisplayName = m.DisplayName,
                SourcePath = m.SourcePath, SizeBytes = m.SizeBytes
            }).ToList()
        };
        var text = JsonSerializer.Serialize(file, JsonOptions);
        if (Encoding.UTF8.GetByteCount(text) > MaxFileBytes)
            throw new ProjectFileException("Projeto maior que o limite permitido para esta prova de conceito.");
        return text;
    }

    public static EditorProject Deserialize(string text)
    {
        if (text is null || Encoding.UTF8.GetByteCount(text) > MaxFileBytes)
            throw new ProjectFileException("Arquivo de projeto vazio ou muito grande.");
        try
        {
            var file = JsonSerializer.Deserialize<ProjectFile>(text, JsonOptions)
                ?? throw new ProjectFileException("Arquivo de projeto vazio.");
            // Backward-compatible with the v6.0 project format (no catalog).
            if (file.FormatVersion is not (1 or FormatVersion))
                throw new ProjectFileException($"Formato {file.FormatVersion} não compatível com esta versão do editor.");
            if (file.Clips is null || file.Markers is null || file.Clips.Count > MaxClips ||
                file.Markers.Count > MaxMarkers || (file.FormatVersion == FormatVersion && file.Media is null) ||
                file.Media?.Count > MaxMedia)
                throw new ProjectFileException("Lista de mídias ou marcadores ausente ou excessiva.");
            if (file.Clips.Any(c => c is null) || file.Markers.Any(m => m is null) ||
                (file.Media?.Any(m => m is null) ?? false))
                throw new ProjectFileException("Projeto contém entradas vazias.");
            var clips = file.Clips.Select(c => new Clip(c.Id, c.MediaId, c.Kind, c.Track,
                new MediaTime(c.StartTicks), new MediaTime(c.DurationTicks), c.Association,
                c.ParentId, new MediaTime(c.ParentOffsetTicks), c.OrphanNotice, c.EffectsToken)).ToArray();
            var markers = file.Markers.Select(m => new TimelineMarker(m.Id, new MediaTime(m.AtTicks), m.Label!)).ToArray();
            var media = file.Media?.Select(m => new MediaAsset(m.Id, m.Kind,
                m.DisplayName ?? string.Empty, m.SourcePath ?? string.Empty, m.SizeBytes)).ToArray()
                ?? Array.Empty<MediaAsset>();
            var result = new EditorProject(file.ProjectId, file.Name ?? string.Empty,
                new TimelineState(file.TimelineVersion, clips, markers), media);
            ValidateProject(result);
            return result;
        }
        catch (JsonException ex) { throw new ProjectFileException("Arquivo JSON inválido ou campos não reconhecidos.", ex); }
        catch (OverflowException ex) { throw new ProjectFileException("Tempo inválido no projeto.", ex); }
    }

    public static EditorProject Load(string path)
    {
        var info = new FileInfo(path);
        if (info.Length > MaxFileBytes)
            throw new ProjectFileException("Arquivo de projeto excede o limite de 16 MB.");
        return Deserialize(File.ReadAllText(path, Encoding.UTF8));
    }

    /// <summary>Save beside the destination as a temporary file, flush then replace.
    /// A failed write does not truncate the previous valid project file.</summary>
    public static void Save(string path, EditorProject project)
    {
        var json = Serialize(project); // validate fully BEFORE touching the filesystem
        var fullPath = Path.GetFullPath(path);
        var folder = Path.GetDirectoryName(fullPath)
            ?? throw new ProjectFileException("Pasta de destino inválida.");
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException($"Pasta não encontrada: {folder}");
        var temporary = Path.Combine(folder, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.WriteThrough))
            using (var writer = new StreamWriter(output, new UTF8Encoding(false)))
            {
                writer.Write(json);
                writer.Flush();
                output.Flush(flushToDisk: true);
            }
            if (File.Exists(fullPath))
                File.Replace(temporary, fullPath, null);
            else
                File.Move(temporary, fullPath);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private sealed class ProjectFile
    {
        public ProjectFile() { }
        public int FormatVersion { get; set; }
        public Guid ProjectId { get; set; }
        public string? Name { get; set; }
        public long TimelineVersion { get; set; }
        public List<ClipFile>? Clips { get; set; }
        public List<MarkerFile>? Markers { get; set; }
        public List<MediaFile>? Media { get; set; }
    }

    private sealed class ClipFile
    {
        public ClipFile() { }
        public Guid Id { get; set; }
        public Guid MediaId { get; set; }
        public MediaKind Kind { get; set; }
        public int Track { get; set; }
        public long StartTicks { get; set; }
        public long DurationTicks { get; set; }
        public AssociationKind Association { get; set; }
        public Guid? ParentId { get; set; }
        public long ParentOffsetTicks { get; set; }
        public bool OrphanNotice { get; set; }
        public string? EffectsToken { get; set; }
    }

    private sealed class MediaFile
    {
        public MediaFile() { }
        public Guid Id { get; set; }
        public MediaKind Kind { get; set; }
        public string? DisplayName { get; set; }
        public string? SourcePath { get; set; }
        public long SizeBytes { get; set; }
    }

    private sealed class MarkerFile
    {
        public MarkerFile() { }
        public Guid Id { get; set; }
        public long AtTicks { get; set; }
        public string? Label { get; set; }
    }
}
