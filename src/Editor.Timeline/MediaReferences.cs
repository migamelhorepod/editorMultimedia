namespace Editor.Timeline;

/// <summary>Metadata referencing a user-selected local file. No media bytes are copied.</summary>
public sealed record MediaAsset(Guid Id, MediaKind Kind, string DisplayName, string SourcePath, long SizeBytes,
    long? DurationTicks = null, int? PixelWidth = null, int? PixelHeight = null);

public sealed class MediaReferenceException(string message) : Exception(message);

/// <summary>Initial POC catalog, not the future IFileAccessService.
/// Bounded MP4/MOV movie duration, WAV PCM/float, and PNG header inspection; no decoding.</summary>
public static class MediaReferences
{
    private static readonly Dictionary<string, MediaKind> Supported = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp4"] = MediaKind.Video, [".mov"] = MediaKind.Video,
        [".mkv"] = MediaKind.Video, [".avi"] = MediaKind.Video,
        [".webm"] = MediaKind.Video,
        [".mp3"] = MediaKind.Audio, [".wav"] = MediaKind.Audio,
        [".m4a"] = MediaKind.Audio, [".aac"] = MediaKind.Audio,
        [".flac"] = MediaKind.Audio, [".ogg"] = MediaKind.Audio,
        [".png"] = MediaKind.Image, [".jpg"] = MediaKind.Image,
        [".jpeg"] = MediaKind.Image, [".webp"] = MediaKind.Image,
        [".bmp"] = MediaKind.Image
    };

    public static bool TryDetectKind(string path, out MediaKind kind) =>
        Supported.TryGetValue(Path.GetExtension(path), out kind);

    /// <summary>Caller must obtain chosenPath from an explicit native file picker.</summary>
    public static MediaAsset InspectFile(string chosenPath)
    {
        if (string.IsNullOrWhiteSpace(chosenPath))
            throw new MediaReferenceException("Nenhum arquivo foi selecionado.");
        string full;
        try { full = Path.GetFullPath(chosenPath); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        { throw new MediaReferenceException("Caminho de mídia inválido."); }
        if (!TryDetectKind(full, out var kind))
            throw new MediaReferenceException("Extensão de mídia não suportada nesta etapa.");
        var info = new FileInfo(full);
        if (!info.Exists || info.Length == 0)
            throw new MediaReferenceException("Arquivo de mídia indisponível ou vazio.");
        var metadata = MediaMetadataProbe.Read(full, kind);
        return new MediaAsset(Guid.NewGuid(), kind, info.Name, full, info.Length,
            metadata.DurationTicks, metadata.PixelWidth, metadata.PixelHeight);
    }

    /// <summary>Presence check on load/import/manual action, never periodic scanning.</summary>
    public static bool IsAvailable(MediaAsset asset)
    {
        try { return new FileInfo(asset.SourcePath).Exists; }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
        { return false; }
    }

    /// <summary>
    /// Explicit user-chosen replacement for a lost reference. Preserves the media ID.
    /// This POC intentionally accepts only the same media kind; cross-kind
    /// replacement needs effect-compatibility rules in the editing engine.
    /// </summary>
    public static IReadOnlyList<MediaAsset> Relink(
        IReadOnlyList<MediaAsset> catalog, Guid mediaId, string chosenPath)
    {
        var index = -1;
        for (var i = 0; i < catalog.Count; i++)
        {
            if (catalog[i].Id == mediaId) { index = i; break; }
        }
        if (index < 0)
            throw new MediaReferenceException("A mídia selecionada não está vinculada ao projeto.");

        var current = catalog[index];
        var replacement = InspectFile(chosenPath);
        if (replacement.Kind != current.Kind)
            throw new MediaReferenceException(
                "Selecione uma mídia do mesmo tipo (vídeo, áudio ou imagem).");

        if (catalog.Any(m => m.Id != mediaId &&
            SameSource(m.SourcePath, replacement.SourcePath)))
            throw new MediaReferenceException(
                "O arquivo não será adicionado pois já existe na biblioteca");

        var updated = catalog.ToArray();
        updated[index] = replacement with { Id = current.Id };
        return updated;
    }

    /// <summary>
    /// Re-read explicitly selected file metadata for an existing catalog entry.
    /// The media ID and any existing timeline edits are left untouched.
    /// </summary>
    public static IReadOnlyList<MediaAsset> RefreshMetadata(
        IReadOnlyList<MediaAsset> catalog, Guid mediaId)
    {
        var index = -1;
        for (var i = 0; i < catalog.Count; i++)
            if (catalog[i].Id == mediaId) { index = i; break; }
        if (index < 0)
            throw new MediaReferenceException("A mídia selecionada não está vinculada ao projeto.");

        var previous = catalog[index];
        if (!IsAvailable(previous))
            throw new MediaReferenceException("A mídia está indisponível; relocalize-a antes de atualizar os dados.");
        var current = InspectFile(previous.SourcePath) with { Id = previous.Id };
        if (current.Kind != previous.Kind)
            throw new MediaReferenceException("O tipo da mídia mudou e não pode ser atualizado.");
        var updated = catalog.ToArray();
        updated[index] = current;
        return updated;
    }

    /// <summary>
    /// Remove a project catalog entry only when no clip refers to it.
    /// The physical file is never deleted.
    /// </summary>
    public static IReadOnlyList<MediaAsset> RemoveUnused(
        IReadOnlyList<MediaAsset> catalog, TimelineState timeline, Guid mediaId)
    {
        if (!catalog.Any(m => m.Id == mediaId))
            throw new MediaReferenceException("A mídia não está vinculada ao projeto.");
        if (timeline.Clips.Any(c => c.MediaId == mediaId))
            throw new MediaReferenceException(
                "Esta mídia ainda possui clipes na timeline e não pode ser removida do catálogo.");
        return catalog.Where(m => m.Id != mediaId).ToArray();
    }

    public static bool SameSource(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    public static void ValidateCatalog(IReadOnlyList<MediaAsset> catalog, TimelineState timeline)
    {
        var ids = new HashSet<Guid>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in catalog)
        {
            if (asset.Id == Guid.Empty || !ids.Add(asset.Id) ||
                string.IsNullOrWhiteSpace(asset.DisplayName) || asset.DisplayName.Length > 255 ||
                asset.DisplayName.Any(char.IsControl) ||
                string.IsNullOrWhiteSpace(asset.SourcePath) || asset.SourcePath.Length > 4096 ||
                asset.SourcePath.Any(char.IsControl) || !Path.IsPathFullyQualified(asset.SourcePath) ||
                !paths.Add(asset.SourcePath) || asset.SizeBytes <= 0 ||
                !TryDetectKind(asset.SourcePath, out var kind) || kind != asset.Kind ||
                Path.GetFileName(asset.SourcePath) != asset.DisplayName ||
                (asset.DurationTicks is long duration &&
                    (asset.Kind is not (MediaKind.Audio or MediaKind.Video) ||
                        duration <= 0 || duration > TimeSpan.FromDays(7).Ticks)) ||
                (asset.PixelWidth.HasValue != asset.PixelHeight.HasValue) ||
                (asset.PixelWidth.HasValue && (asset.Kind != MediaKind.Image ||
                    asset.PixelWidth.Value is <= 0 or > 100000 ||
                    asset.PixelHeight!.Value is <= 0 or > 100000)))
                throw new MediaReferenceException("Arquivo ou identificador de mídia inválido ou duplicado.");
        }
        foreach (var clip in timeline.Clips)
        {
            var media = catalog.FirstOrDefault(m => m.Id == clip.MediaId);
            if (media is null) continue; // Historical demonstration clip without a physical source.
            if (media.Kind != clip.Kind)
                throw new MediaReferenceException("O tipo de mídia do clipe não corresponde ao arquivo registrado.");
            if (clip.SourceStart.Ticks < 0)
                throw new MediaReferenceException("O corte começa antes do início do arquivo original.");
            if (clip.Kind == MediaKind.Image)
            {
                if (clip.SourceStart != MediaTime.Zero)
                    throw new MediaReferenceException("Imagens não podem ter deslocamento temporal de origem.");
            }
            else if (media.DurationTicks is long fullLength &&
                     ((decimal)clip.SourceStart.Ticks + clip.Duration.Ticks > fullLength))
                throw new MediaReferenceException("O clipe ultrapassa a duração do arquivo original.");
        }
    }

    /// <summary>Placeholder durations until media probing in POC-002.</summary>
    public static Clip PlanClip(TimelineState state, MediaAsset asset, MediaTime playhead, Guid clipId)
    {
        TimelineEngine.Validate(state);
        if (playhead.Ticks < 0 || clipId == Guid.Empty)
            throw new MediaReferenceException("Posição de inserção inválida.");
        // Videos must enter the main track at their real source duration.
        // Refuse unknown video duration instead of silently truncating to 5s.
        if (asset.Kind is MediaKind.Video or MediaKind.Audio && asset.DurationTicks is null)
            throw new MediaReferenceException(
                "Não foi possível determinar a duração real da mídia temporal. " +
                "Instale ou configure ffprobe para analisar formatos comprimidos; nenhum clipe foi inserido.");
        var duration = asset.DurationTicks is long ticks
            ? new MediaTime(ticks)
            : MediaTime.FromSeconds(asset.Kind == MediaKind.Image ? 4 : 5);
        var start = asset.Kind == MediaKind.Video
            ? state.Clips.Where(c => c.Track == TimelineEngine.MainTrack)
                .Select(c => c.End).DefaultIfEmpty(MediaTime.Zero).Max()
            : playhead;
        var track = TimelineEngine.MainTrack;
        if (asset.Kind != MediaKind.Video)
        {
            var end = start + duration;
            track = Enumerable.Range(1, TimelineEngine.MaxTracks - 1)
                .Where(lane => state.Clips.Where(c => c.Track == lane)
                    .All(c => end.Ticks <= c.Start.Ticks || start.Ticks >= c.End.Ticks))
                .DefaultIfEmpty(-1).First();
            if (track < 0) throw new TimelineException("NO_FREE_TRACK", "Nenhuma trilha secundária disponível.");
        }
        return new Clip(clipId, asset.Id, asset.Kind, track, start, duration);
    }
}
