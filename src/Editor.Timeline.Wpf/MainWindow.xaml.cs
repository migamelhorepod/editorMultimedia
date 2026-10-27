using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Editor.Timeline;
using Editor.Security;
using System.Windows.Threading;
using System.Threading.Tasks;

namespace Editor.Timeline.Wpf;

public partial class MainWindow : Window
{
    private const double HeaderWidth = 120;
    private double playheadSeconds;
    private const double SnapStepSeconds = 0.5;
    private const double RulerHeight = 38;
    private const double TrackHeight = 47;
    private TimelineSession session;
    private Guid projectId;
    private string projectName = "Projeto sem título";
    private string? projectPath;
    private TimelineState savedState;
    private List<MediaAsset> mediaAssets = new();
    private IReadOnlyList<MediaAsset> savedMedia = Array.Empty<MediaAsset>();
    private readonly Dictionary<Guid, bool> availableMedia = new();
    private Guid? catalogSelectionId;
    private readonly MediaImportCoordinator importCoordinator = new(Guid.NewGuid());
    private LibraryStorageMonitor? libraryStorageMonitor;
    private string libraryRootPath = @"D:\";
    private readonly DispatcherTimer storageTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly Dictionary<Guid, IDisposable> activeWorkerJobs = new();

    // A project catalog entry may have zero, one or many timeline occurrences.
    private sealed record MediaListItem(MediaAsset Asset, int Usages, bool Missing)
    {
        public string Name => Asset.DisplayName;
        public string Details => $"{Asset.Kind} · {Usages} ocorrência(s) na timeline" +
            (Missing ? " · MÍDIA INDISPONÍVEL" : "") +
            (Asset.DurationTicks is long ticks
                ? $" · {Timecode.Format(new MediaTime(ticks))} (duração original)"
                : Asset.Kind is MediaKind.Audio or MediaKind.Video
                    ? " · duração não aferida" : "") +
            (Asset.PixelWidth is int width && Asset.PixelHeight is int height
                ? $" · PNG {width}×{height}px"
                : "") +
            $" · {Asset.SizeBytes / 1024.0:0.#} KB";
    }
    private bool hasUnsavedChanges;
    private Guid? selectedId;
    private Guid? draggingId;
    private double dragStartX;
    private double dragStartY;
    private double originalSeconds;
    private int originalTrack;
    private double dragPreviewSeconds;
    private int dragPreviewTrack;
    private bool hasDragPreview;
    private bool draggingMain;
    private int originalMainIndex;
    private int previewMainIndex;
    private TimelineState? previewState;
    private string? previewError;
    private int? resolvedPreviewTrack;
    // Both edge-resize gestures share one non-mutating preview and one undoable commit.
    // The preview never enters TimelineSession or its undo history.
    private bool resizingClip;
    private bool resizingLeftEdge;
    private MediaTime originalResizeStart;
    private MediaTime resizePreviewStart;
    private MediaTime originalResizeDuration;
    private MediaTime resizePreviewDuration;

    // Ruler scrubbing and marker drag are purely visual until mouse-up.
    // Markers themselves are part of the TimelineSession undo/redo history.
    private bool scrubbingRuler;
    private Guid? draggingMarkerId;
    private MediaTime markerDragOriginal;
    private MediaTime markerDragPreview;
    private double preDragPlayhead;

    private Button? duplicateToolbarButton;
    private Button? deleteToolbarButton;
    private Button? independentToolbarButton;




    public MainWindow()
    {
        InitializeComponent();
        libraryStorageMonitor = CreateStorageMonitor(libraryRootPath);
        libraryStorageMonitor.Check();
        RecoverLibraryCopiesAtStartup();
        storageTimer.Tick += (_, _) =>
        {
            if (libraryStorageMonitor is null) return;
            if (!libraryStorageMonitor.Check())
            {
                foreach (var media in mediaAssets)
                {
                    if (media.SourcePath.StartsWith(libraryRootPath, StringComparison.OrdinalIgnoreCase))
                        availableMedia[media.Id] = false;
                }
                Draw();
            }
        };
        storageTimer.Start();
        session = new TimelineSession(new TimelineState(0, Array.Empty<Clip>()));
        savedState = session.Current;
        projectId = Guid.NewGuid();
        InitializeClipActionsToolbar();
        UpdateWindowTitle();
        Draw();
    }

    private void UpdateWindowTitle()
    {
        Title = $"Editor Multimídia — {projectName}{(hasUnsavedChanges ? " *" : "")} — POC-003.2 (TM-06 corrigido)";
    }

    private void UpdateDirty()
    {
        hasUnsavedChanges = !ProjectStore.SameContent(session.Current, savedState) ||
            !mediaAssets.SequenceEqual(savedMedia);
        UpdateWindowTitle();
    }

    private static string NameFromPath(string path)
    {
        var file = System.IO.Path.GetFileName(path) ?? string.Empty;
        if (file.EndsWith(".emproj.json", StringComparison.OrdinalIgnoreCase))
            return file[..^".emproj.json".Length];
        return System.IO.Path.GetFileNameWithoutExtension(file) ?? string.Empty;
    }

    // Save is intentionally explicit: there is no timer, auto-save or write on close.
    private bool SaveProject(bool choosePath = false)
    {
        var destination = choosePath ? null : projectPath;
        if (destination is null)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Salvar projeto multimídia",
                Filter = "Projeto Editor Multimídia (*.emproj.json)|*.emproj.json",
                DefaultExt = ".emproj.json",
                AddExtension = true,
                OverwritePrompt = true,
                FileName = projectPath is null ? $"{projectName}.emproj.json" : System.IO.Path.GetFileName(projectPath)
            };
            if (dialog.ShowDialog(this) != true) return false;
            destination = dialog.FileName;
        }
        try
        {
            var name = NameFromPath(destination);
            ProjectStore.Save(destination, new EditorProject(projectId, name, session.Current, mediaAssets.ToArray()));
            projectPath = destination;
            projectName = name;
            savedState = session.Current;
            savedMedia = mediaAssets.ToArray();
            hasUnsavedChanges = false;
            UpdateWindowTitle();
            Draw();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or ProjectFileException or ArgumentException or NotSupportedException)
        {
            MessageBox.Show(this, $"Não foi possível salvar o projeto\n{ex.Message}",
                "Falha ao salvar", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private bool ConfirmBeforeReplacingProject()
    {
        if (!hasUnsavedChanges) return true;
        var answer = MessageBox.Show(this,
            "Existem alterações não salvas\nDeseja salvar o projeto antes de continuar?",
            "Alterações não salvas", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return answer switch
        {
            MessageBoxResult.Yes => SaveProject(),
            MessageBoxResult.No => true,
            _ => false
        };
    }

    private void ResetLoadedProject(EditorProject project, string? path)
    {
        // A replacement is committed only after the file was fully parsed and validated.
        session = new TimelineSession(project.Timeline, project.MediaItems);
        projectId = project.Id;
        projectName = project.Name;
        projectPath = path;
        savedState = session.Current;
        catalogSelectionId = null;
        mediaAssets = session.Media.ToList();
        savedMedia = mediaAssets.ToArray();
        RefreshMediaPresence();
        hasUnsavedChanges = false;
        selectedId = null;
        draggingId = null;
        draggingMarkerId = null;
        scrubbingRuler = false;
        hasDragPreview = false;
        previewState = null;
        previewError = null;
        draggingMain = false;
        resizingClip = false;
        resizingLeftEdge = false;
        Surface.ReleaseMouseCapture();
        playheadSeconds = 0;
        SnapToggle.IsChecked = false; // approved default, not persisted in project file
        Viewport.ScrollToHorizontalOffset(0);
        Viewport.ScrollToVerticalOffset(0);
        UpdateWindowTitle();
        Draw();
    }

    private void NewProject_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmBeforeReplacingProject()) return;
        ResetLoadedProject(new EditorProject(Guid.NewGuid(), "Projeto sem título",
            new TimelineState(0, Array.Empty<Clip>())), null);
    }

    private void NewDemo_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmBeforeReplacingProject()) return;
        ResetLoadedProject(new EditorProject(Guid.NewGuid(), "Demonstração", CreateDemo()), null);
    }

    private void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmBeforeReplacingProject()) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Abrir projeto multimídia",
            Filter = "Projeto Editor Multimídia (*.emproj.json)|*.emproj.json",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var project = ProjectStore.Load(dialog.FileName);
            ResetLoadedProject(project, dialog.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or ProjectFileException or ArgumentException or NotSupportedException)
        {
            MessageBox.Show(this,
                $"Não foi possível abrir o projeto\n{ex.Message}",
                "Falha ao abrir", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RefreshMediaPresence()
    {
        availableMedia.Clear();
        foreach (var asset in mediaAssets)
            availableMedia[asset.Id] = MediaReferences.IsAvailable(asset);
    }

    private void CheckMedia_Click(object sender, RoutedEventArgs e)
    {
        RefreshMediaPresence();
        Draw();
        var missing = availableMedia.Count(pair => !pair.Value);
        MessageBox.Show(this, missing == 0
            ? $"{mediaAssets.Count} mídias vinculadas; nenhuma indisponível nesta verificação."
            : $"{missing} mídia(s) indisponível(is)\nSelecione um clipe indisponível e use Arquivo > Relocalizar mídia selecionada",
            "Verificação de mídias", MessageBoxButton.OK,
            missing == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private async void ImportMedia_Click(object sender, RoutedEventArgs e)
    {
        if (libraryStorageMonitor is null || !libraryStorageMonitor.Check())
        {
            MessageBox.Show(this, "A biblioteca ativa está indisponível. Verifique a pasta no disco D:.",
                "Biblioteca indisponível", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var picker = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Selecionar arquivos de vídeo, áudio ou imagem",
            Filter = "Mídias compatíveis|*.mp4;*.mov;*.mkv;*.avi;*.webm;*.mp3;*.wav;*.m4a;*.aac;*.flac;*.ogg;*.png;*.jpg;*.jpeg;*.webp;*.bmp|Todos os arquivos|*.*",
            CheckFileExists = true, Multiselect = true
        };
        if (picker.ShowDialog(this) != true) return;

        var importTask = importCoordinator.BeginImport();
        ImportStatus.Text = "Importação em andamento — aguarde a conclusão. O processamento não pode ser cancelado.";
        try
        {
        var added = 0;
        var ignored = 0;
        var errors = new List<string>();
        foreach (var path in picker.FileNames)
        {
            try
            {
                // External media is first copied into the active physical library
                // with a private partial file and an atomic final rename.
                var stored = await new LibraryFileStore(libraryRootPath).ImportAsync(path);
                MediaAsset asset;
                try
                {
                    // All actual probing/decoding of new media executes in the
                    // verified AppContainer worker; never in the WPF process.
                    asset = await InspectLibraryAssetInSandboxAsync(stored.Path);
                }
                catch (Exception ex) when (ex is MediaReferenceException or
                    NotSupportedException or InvalidDataException or ArgumentException)
                {
                    // An invalid NEW import must not pollute the library.
                    // Existing/reused media and the original are never deleted.
                    if (stored.NewlyCopied)
                    {
                        try { if (File.Exists(stored.Path)) File.Delete(stored.Path); }
                        catch (IOException) { /* The import error remains primary. */ }
                        catch (UnauthorizedAccessException) { /* Cleanup may retry during maintenance. */ }
                    }
                    throw;
                }
                if (libraryStorageMonitor is not null && !libraryStorageMonitor.Check())
                    throw new IOException("Biblioteca indisponível durante a importação.");
                if (mediaAssets.Any(m => MediaReferences.SameSource(m.SourcePath, asset.SourcePath)))
                {
                    ignored++;
                    continue;
                }
                var proposed = MediaReferences.PlanClip(session.Current, asset,
                    MediaTime.FromSeconds((decimal)playheadSeconds), Guid.NewGuid());
                // Commit first; catalog entry is recorded only if insertion succeeds.
                session.Execute(state => TimelineEngine.Insert(state, state.Version, proposed),
                    mediaAssets.Append(asset).ToArray());
                mediaAssets = session.Media.ToList();
                availableMedia[asset.Id] = true;
                selectedId = proposed.Id;
                catalogSelectionId = asset.Id;
                added++;
            }
            catch (Exception ex) when (ex is MediaReferenceException or TimelineException or
                IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or OverflowException or
                Win32Exception or System.Runtime.InteropServices.COMException)
            {
                errors.Add($"{System.IO.Path.GetFileName(path)}: {ex.Message}");
            }
        }
        UpdateDirty();
        Draw();
        var duplicateMessage = ignored > 0
            ? "\nO arquivo não será adicionado pois já existe na biblioteca" : "";
        var issueText = errors.Count == 0 ? "" : "\n\nNão importados:\n" +
            string.Join("\n", errors.Take(10));
        MessageBox.Show(this,
            $"{added} arquivo(s) vinculado(s) à timeline{duplicateMessage}{issueText}",
            "Importação de mídias", MessageBoxButton.OK,
            errors.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
        finally
        {
            importCoordinator.CompleteImport(importTask);
            ImportStatus.Text = "";
        }
    }


    /// <summary>Only the isolated worker opens and decodes the media.</summary>
    private Task<MediaAsset> InspectLibraryAssetInSandboxAsync(string sourcePath)
    {
        string activeRoot = libraryRootPath;
        Guid activeLibraryId = importCoordinator.ActiveLibrary;
        return Task.Run(() =>
        {
            if (!MediaReferences.TryDetectKind(sourcePath, out var kind))
                throw new MediaReferenceException("Extensão não suportada.");
            var file = new FileInfo(sourcePath);
            if (!file.Exists || file.Length <= 0)
                throw new MediaReferenceException("Arquivo indisponível ou vazio.");
            var worker = System.IO.Path.Combine(AppContext.BaseDirectory,
                "worker", "Editor.Security.Worker.exe");
            var service = new IsolatedMediaProcessingService(activeRoot, activeLibraryId, worker);
            long? measured = null;
            if (kind is MediaKind.Video or MediaKind.Audio)
                measured = service.InspectDurationTicks(file.FullName);
            else
                service.DecodeToNull(file.FullName);
            return new MediaAsset(Guid.NewGuid(), kind, file.Name, file.FullName, file.Length, measured);
        });
    }

    private MediaAsset? CatalogSelection() =>
        (MediaList?.SelectedItem as MediaListItem)?.Asset;

    private void RefreshCatalogPanel()
    {
        if (MediaList is null || MediaSearchBox is null || MediaKindFilter is null ||
            MediaSummary is null) return;
        var requestedId = (MediaList.SelectedItem as MediaListItem)?.Asset.Id ?? catalogSelectionId;
        var filter = MediaSearchBox.Text.Trim();
        var category = MediaKindFilter.SelectedIndex;
        var rows = mediaAssets
            .Where(m => (filter.Length == 0 ||
                m.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                m.SourcePath.Contains(filter, StringComparison.OrdinalIgnoreCase)))
            .Where(m => category switch
            {
                1 => m.Kind == MediaKind.Video,
                2 => m.Kind == MediaKind.Audio,
                3 => m.Kind == MediaKind.Image,
                4 => !availableMedia.TryGetValue(m.Id, out var found) || !found,
                _ => true
            })
            .OrderBy(m => m.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(m => m.Id)
            .Select(m => new MediaListItem(
                m,
                session.Current.Clips.Count(c => c.MediaId == m.Id),
                !availableMedia.TryGetValue(m.Id, out var found) || !found))
            .ToArray();
        MediaList.ItemsSource = rows;
        var previous = rows.FirstOrDefault(row => row.Asset.Id == requestedId);
        MediaList.SelectedItem = previous;
        MediaSummary.Text = $"{rows.Length} de {mediaAssets.Count} mídia(s) · " +
            $"{mediaAssets.Count(m => !availableMedia.TryGetValue(m.Id, out var available) || !available)} indisponível(is)";
        UpdateCatalogActions();
    }

    private void UpdateCatalogActions()
    {
        if (InsertMediaButton is null || LocateMediaButton is null ||
            RelinkMediaButton is null || RefreshMetadataButton is null ||
            FitSelectedClipButton is null) return;
        var item = MediaList?.SelectedItem as MediaListItem;
        var hasSelected = item is not null;
        var uses = item?.Usages ?? 0;
        InsertMediaButton.IsEnabled = hasSelected && !item!.Missing;
        LocateMediaButton.IsEnabled = uses > 0;
        RelinkMediaButton.IsEnabled = hasSelected;
        RefreshMetadataButton.IsEnabled = hasSelected && !item!.Missing;
        var selectedClip = Selected();
        FitSelectedClipButton.IsEnabled = selectedClip is { Kind: MediaKind.Video or MediaKind.Audio } &&
            mediaAssets.Any(m => m.Id == selectedClip.MediaId);
    }

    private void MediaSearch_Changed(object sender, TextChangedEventArgs e)
    {
        if (session is not null) RefreshCatalogPanel();
    }

    private void MediaFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (session is not null) RefreshCatalogPanel();
    }

    private void MediaSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (MediaList?.SelectedItem is MediaListItem item)
            catalogSelectionId = item.Asset.Id;
        UpdateCatalogActions();
    }

    private void MediaList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        InsertFromCatalog_Click(sender, new RoutedEventArgs());
    }

    private void InsertFromCatalog_Click(object sender, RoutedEventArgs e)
    {
        var asset = CatalogSelection();
        if (asset is null) return;
        if (!MediaReferences.IsAvailable(asset))
        {
            availableMedia[asset.Id] = false;
            Draw();
            MessageBox.Show(this,
                "Mídia indisponível\nRelocalize a referência antes de inseri-la",
                "Mídia indisponível", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try
        {
            var planned = MediaReferences.PlanClip(session.Current, asset,
                MediaTime.FromSeconds((decimal)playheadSeconds), Guid.NewGuid());
            session.Execute(state => TimelineEngine.Insert(state, state.Version, planned));
            selectedId = planned.Id;
            UpdateDirty();
            Draw();
        }
        catch (Exception ex) when (ex is TimelineException or MediaReferenceException or OverflowException)
        {
            MessageBox.Show(this, ex is TimelineException { Code: "NO_FREE_TRACK" }
                ? "Não é possível criar mais trilhas\n\nConsidere realocar a mídia em uma trilha já existente"
                : ex.Message,
                "Não foi possível inserir a mídia", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void LocateCatalogClip_Click(object sender, RoutedEventArgs e)
    {
        var asset = CatalogSelection();
        if (asset is null) return;
        var clip = session.Current.Clips.Where(c => c.MediaId == asset.Id)
            .OrderBy(c => c.Start.Ticks).ThenBy(c => c.Track).FirstOrDefault();
        if (clip is null) return;
        selectedId = clip.Id;
        Draw();
        Viewport.ScrollToHorizontalOffset(Math.Max(0, X(clip.Start) - HeaderWidth - 40));
        Viewport.ScrollToVerticalOffset(Math.Max(0, Y(clip.Track) - RulerHeight));
    }

    private void MediaList_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject origin) return;
        var container = ItemsControl.ContainerFromElement(MediaList, origin) as ListBoxItem;
        if (container?.DataContext is not MediaListItem item) return;
        MediaList.SelectedItem = item;
        catalogSelectionId = item.Asset.Id;
        var menu = new ContextMenu
        {
            PlacementTarget = container,
            Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint
        };
        var delete = new MenuItem { Header = "Excluir" };
        var mediaId = item.Asset.Id;
        delete.Click += (_, _) => DeleteCatalogMedia(mediaId);
        menu.Items.Add(delete);
        menu.IsOpen = true;
        e.Handled = true;
    }

    private bool ConfirmCatalogDeletion()
    {
        var prompt = new Window
        {
            Title = "Confirmar exclusão",
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            MinWidth = 470,
            ShowInTaskbar = false
        };
        var body = new StackPanel { Margin = new Thickness(20) };
        body.Children.Add(new TextBlock
        {
            Text = "Se a mídia for excluída do catálogo, também será removida da timeline, quer mesmo excluir?",
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 510,
            Margin = new Thickness(0, 0, 0, 18)
        });
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var cancel = new Button { Content = "Cancelar", MinWidth = 95, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var confirm = new Button { Content = "Confirmar", MinWidth = 95, IsDefault = true };
        cancel.Click += (_, _) => prompt.DialogResult = false;
        confirm.Click += (_, _) => prompt.DialogResult = true;
        actions.Children.Add(cancel);
        actions.Children.Add(confirm);
        body.Children.Add(actions);
        prompt.Content = body;
        return prompt.ShowDialog() == true;
    }

    private void DeleteCatalogMedia(Guid mediaId)
    {
        var media = mediaAssets.FirstOrDefault(m => m.Id == mediaId);
        if (media is null) return;
        var used = session.Current.Clips.Any(c => c.MediaId == mediaId);
        if (used)
        {
            if (!ConfirmCatalogDeletion()) return;
        }
        try
        {
            session.DeleteCatalogMedia(mediaId);
            mediaAssets = session.Media.ToList();
            availableMedia.Remove(mediaId);
            if (catalogSelectionId == mediaId) catalogSelectionId = null;
            if (selectedId is Guid clipId && session.Current.Clips.All(c => c.Id != clipId))
                selectedId = null;
            UpdateDirty();
            Draw();
        }
        catch (Exception ex) when (ex is MediaReferenceException or TimelineException)
        {
            MessageBox.Show(this, ex.Message, "Não foi possível excluir",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RefreshCatalogMetadata_Click(object sender, RoutedEventArgs e)
    {
        var asset = CatalogSelection();
        if (asset is null) return;
        try
        {
            session.RefreshMediaMetadata(asset.Id);
            mediaAssets = session.Media.ToList();
            availableMedia[asset.Id] = MediaReferences.IsAvailable(asset);
            UpdateDirty();
            Draw();
        }
        catch (Exception ex) when (ex is MediaReferenceException or TimelineException or
            IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            MessageBox.Show(this, ex.Message, "Não foi possível atualizar os dados",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void FitSelectedClip_Click(object sender, RoutedEventArgs e)
    {
        var clip = Selected();
        if (clip is null) return;
        try
        {
            session.FitClipToSource(clip.Id);
            mediaAssets = session.Media.ToList();
            RefreshMediaPresence();
            UpdateDirty();
            Draw();
        }
        catch (Exception ex) when (ex is MediaReferenceException or TimelineException or
            IOException or UnauthorizedAccessException or ArgumentException or
            NotSupportedException or OverflowException)
        {
            // Context-menu timeline actions do not interrupt the editor with
            // dialogs. Failure is atomic; keep the source and current edits.
            System.Diagnostics.Debug.WriteLine($"Restaurar duração original: {ex}");
        }
    }

    private void RelinkCatalogMedia_Click(object sender, RoutedEventArgs e)
    {
        var asset = CatalogSelection();
        if (asset is not null) RelinkAsset(asset);
    }

    private void RelinkSelectedMedia_Click(object sender, RoutedEventArgs e)
    {
        var clip = Selected();
        if (clip is null) return;
        var media = mediaAssets.FirstOrDefault(m => m.Id == clip.MediaId);
        if (media is not null) RelinkAsset(media);
    }

    private void RelinkAsset(MediaAsset media)
    {
        var filter = media.Kind switch
        {
            MediaKind.Video => "Arquivos de vídeo|*.mp4;*.mov;*.mkv;*.avi;*.webm",
            MediaKind.Audio => "Arquivos de áudio|*.mp3;*.wav;*.m4a;*.aac;*.flac;*.ogg",
            MediaKind.Image => "Arquivos de imagem|*.png;*.jpg;*.jpeg;*.webp;*.bmp",
            _ => "Todos os arquivos|*.*"
        };
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = $"Relocalizar {media.DisplayName} — selecionar novo arquivo",
            Filter = filter + "|Todos os arquivos|*.*",
            CheckFileExists = true, Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            session.RelinkMedia(media.Id, dialog.FileName);
            mediaAssets = session.Media.ToList();
            RefreshMediaPresence();
            UpdateDirty();
            Draw();
        }
        catch (Exception ex) when (ex is MediaReferenceException or TimelineException or
            IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or OverflowException)
        {
            MessageBox.Show(this,
                $"Não foi possível relocalizar a mídia, a referência original foi preservada\n{ex.Message}",
                "Relocalização não realizada", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SaveProject_Click(object sender, RoutedEventArgs e) => SaveProject();
    private void SaveAsProject_Click(object sender, RoutedEventArgs e) => SaveProject(choosePath: true);

    private LibraryStorageMonitor CreateStorageMonitor(string root) =>
        new(importCoordinator, root, id =>
        {
            if (activeWorkerJobs.Remove(id, out var job)) job.Dispose();
        });

    private void RecoverLibraryCopiesAtStartup()
    {
        if (libraryStorageMonitor is null || !libraryStorageMonitor.Check()) return;
        try
        {
            int removed = new LibraryFileStore(libraryRootPath).RecoverInterruptedImports();
            if (removed > 0)
                MessageBox.Show(this,
                    $"{removed} importação(ões) interrompida(s) foram descartadas. Nenhuma mídia concluída foi apagada.",
                    "Recuperação da biblioteca", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "Não foi possível recuperar importações interrompidas: " + ex.Message,
                "Recuperação da biblioteca", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ChangeLibrary_Click(object sender, RoutedEventArgs e)
    {
        if (importCoordinator.ActiveImports > 0)
        {
            ShowBusyLibraryModal();
            return;
        }
        var picker = new Microsoft.Win32.OpenFolderDialog { Title = "Selecionar biblioteca de mídias" };
        if (picker.ShowDialog(this) != true) return;
        if (!System.IO.Path.GetFullPath(picker.FolderName).StartsWith(@"D:\", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "Selecione uma pasta de biblioteca dentro do disco D:",
                "Biblioteca", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var result = importCoordinator.TryChangeLibrary(Guid.NewGuid());
        if (!result.Allowed) { ShowBusyLibraryModal(); return; }
        libraryRootPath = picker.FolderName;
        libraryStorageMonitor = CreateStorageMonitor(libraryRootPath);
        libraryStorageMonitor.Check();
        RecoverLibraryCopiesAtStartup();
    }

    private void DeactivateLibrary_Click(object sender, RoutedEventArgs e)
    {
        var result = importCoordinator.TryChangeLibrary(null);
        if (!result.Allowed) { ShowBusyLibraryModal(); return; }
        libraryStorageMonitor = null;
    }

    private void ShowBusyLibraryModal() =>
        MessageBox.Show(this, MediaImportCoordinator.BusyMessage,
            "Biblioteca em processamento", MessageBoxButton.OK, MessageBoxImage.Information);

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (importCoordinator.ActiveImports > 0)
        {
            e.Cancel = true;
            ShowBusyLibraryModal();
            return;
        }
        if (!ConfirmBeforeReplacingProject()) e.Cancel = true;
    }

    private static TimelineState CreateDemo()
    {
        var a = new Clip(Guid.NewGuid(), Guid.NewGuid(), MediaKind.Video, 0, MediaTime.Zero, MediaTime.FromSeconds(8));
        var b = new Clip(Guid.NewGuid(), Guid.NewGuid(), MediaKind.Video, 0, MediaTime.FromSeconds(8), MediaTime.FromSeconds(9));
        var c = new Clip(Guid.NewGuid(), Guid.NewGuid(), MediaKind.Video, 0, MediaTime.FromSeconds(17), MediaTime.FromSeconds(7));
        var subtitle = new Clip(Guid.NewGuid(), Guid.NewGuid(), MediaKind.Image, 1, MediaTime.FromSeconds(2), MediaTime.FromSeconds(4), AssociationKind.Positioned, a.Id, MediaTime.FromSeconds(2));
        var audio = new Clip(Guid.NewGuid(), Guid.NewGuid(), MediaKind.Audio, 2, MediaTime.FromSeconds(3), MediaTime.FromSeconds(15), AssociationKind.Positioned, a.Id, MediaTime.FromSeconds(3));
        var title = new Clip(Guid.NewGuid(), Guid.NewGuid(), MediaKind.Image, 3, MediaTime.FromSeconds(19), MediaTime.FromSeconds(4), AssociationKind.Positioned, c.Id, MediaTime.FromSeconds(2));
        // Demo-only orphan fixture: allows visual verification of the inline
        // warning, drag to clear it, and undo to bring it back. No media is missing.
        var orphanSample = new Clip(Guid.NewGuid(), Guid.NewGuid(), MediaKind.Image, 4,
            MediaTime.FromSeconds(9), MediaTime.FromSeconds(4), OrphanNotice: true);
        return new TimelineState(0, new[] { a, b, c, subtitle, audio, title, orphanSample });
    }

    private double Scale => ZoomSlider?.Value ?? 25;
    private static double Seconds(MediaTime time) => (double)time.Seconds;
    private double X(MediaTime time) => HeaderWidth + Seconds(time) * Scale;
    private static double Y(int track) => RulerHeight + track * TrackHeight;

    private void Draw()
    {
        if (Surface is null || session is null) return;
        RefreshCatalogPanel();
        // Atualizar disponibilidade em conjunto com a seleção, inclusive após Undo/Redo.
        var selected = Selected();
        if (duplicateToolbarButton is not null)
            duplicateToolbarButton.IsEnabled = selected is { Track: > TimelineEngine.MainTrack };
        if (deleteToolbarButton is not null)
            deleteToolbarButton.IsEnabled = selected is not null;
        if (independentToolbarButton is not null)
            independentToolbarButton.IsEnabled = selected is { Track: > TimelineEngine.MainTrack,
                Association: AssociationKind.Positioned };
        Surface.Children.Clear();
        var visualState = (draggingMain || resizingClip) && hasDragPreview && previewState is not null
            ? previewState : session.Current;
        // Reserve enough canvas space for the live resize preview as well.
        var rightmostMarker = session.Current.MarkerItems.Count == 0 ? 0 :
            session.Current.MarkerItems.Max(m => Seconds(m.At));
        var duration = Math.Max(40, Math.Max(Seconds(visualState.Duration), rightmostMarker) + 12);
        Surface.Width = Math.Max(850, HeaderWidth + duration * Scale + 30);
        Surface.Height = RulerHeight + TimelineEngine.MaxTracks * TrackHeight;
        for (var track = 0; track < TimelineEngine.MaxTracks; track++)
        {
            var row = new Rectangle { Width = Surface.Width, Height = TrackHeight - 1,
                Fill = Brush(track % 2 == 0 ? "#1B2230" : "#202838") };
            Canvas.SetTop(row, Y(track)); Surface.Children.Add(row);
            var header = new Rectangle { Width = HeaderWidth - 1, Height = TrackHeight - 1, Fill = Brush("#303B4D") };
            Canvas.SetTop(header, Y(track)); Surface.Children.Add(header);
            AddText(track == 0 ? "Principal" : $"Trilha {track + 1}", 12, Y(track) + 14, Brushes.White, 12);
        }
        for (int second = 0; second <= duration; second += 5)
        {
            var pos = HeaderWidth + second * Scale;
            var line = new Line { X1 = pos, X2 = pos, Y1 = RulerHeight - 5, Y2 = Surface.Height,
                Stroke = Brush("#435064"), StrokeThickness = 0.6 };
            Surface.Children.Add(line);
            // Avoid overlapping labels on low zoom; keep seconds readable above 1 hour.
            var rulerStep = Math.Max(5, (int)Math.Ceiling(95.0 / Scale / 5.0) * 5);
            if (second % rulerStep == 0)
                AddText(Timecode.Format(MediaTime.FromSeconds(second)),
                    pos + 3, 10, Brush("#C6CFDC"), 11);
        }
        // Timeline markers are guides at absolute instants; they never shift when
        // clips are reorganized and they never affect clip overlap validation.
        foreach (var marker in session.Current.MarkerItems.OrderBy(m => m.At.Ticks))
        {
            var time = draggingMarkerId == marker.Id ? markerDragPreview : marker.At;
            var markerX = X(time);
            var stem = new Line { X1 = markerX, X2 = markerX, Y1 = 30,
                Y2 = Surface.Height, Stroke = Brush("#E0AA55"), StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 3, 5 }, Opacity = 0.65,
                IsHitTestVisible = false };
            Surface.Children.Add(stem);
            var diamond = new Polygon
            {
                Points = new PointCollection { new(0, 0), new(6, 7), new(0, 14), new(-6, 7) },
                Fill = Brush("#FFB55A"), Stroke = Brush("#663B1D"), StrokeThickness = 1,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(diamond, markerX); Canvas.SetTop(diamond, 16);
            Surface.Children.Add(diamond);
            // Transparent hit-area provides a tooltip while mouse events bubble
            // to the ruler, where actual marker gestures are handled.
            var markerHit = new Border
            {
                Width = 18, Height = 23, Background = Brushes.Transparent,
                ToolTip = $"{marker.Label} · {time.Seconds:0.0}s"
            };
            Canvas.SetLeft(markerHit, markerX - 9);
            Canvas.SetTop(markerHit, 9);
            Surface.Children.Add(markerHit);
        }
        // The playhead is a visual cursor only: it never mutates the timeline state.
        var playheadLine = new Line { X1 = HeaderWidth + playheadSeconds * Scale,
            X2 = HeaderWidth + playheadSeconds * Scale, Y1 = 0, Y2 = Surface.Height,
            Stroke = Brush("#F0C85B"), StrokeThickness = 2, IsHitTestVisible = false };
        Surface.Children.Add(playheadLine);
        foreach (var clip in visualState.Clips)
        {
            bool preview = hasDragPreview && draggingId == clip.Id;
            var start = preview && !draggingMain && !resizingClip ? dragPreviewSeconds : Seconds(clip.Start);
            var track = preview && !draggingMain && !resizingClip ? resolvedPreviewTrack ?? dragPreviewTrack : clip.Track;
            // The warning belongs to the clip, not to a separate acknowledgement
            // command. Use a compact overlay that can be reused for other media
            // conditions (e.g. unavailable media) in later POCs.
            var linked = mediaAssets.FirstOrDefault(m => m.Id == clip.MediaId);
            var missingMedia = linked is not null && (!availableMedia.TryGetValue(linked.Id, out var present) || !present);
            var hasWarning = clip.OrphanNotice || missingMedia;
            var content = new Grid();
            content.Children.Add(new TextBlock
            {
                Text = linked is null ? $"{clip.Kind}  {Timecode.Format(clip.Duration)}"
                    : $"{linked.DisplayName}  {Timecode.Format(clip.Duration)}",
                Foreground = Brushes.White,
                Margin = new Thickness(7, 8, hasWarning ? 23 : 2, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            // Two resize grips, with the clip body reserved for moving/selection.
            // Very short clips need a higher zoom so their gestures remain distinct.
            if (Seconds(clip.Duration) * Scale >= 28)
            {
                content.Children.Add(new Border
                {
                    Width = 7,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Margin = new Thickness(2, 5, 0, 5),
                    CornerRadius = new CornerRadius(2),
                    Background = Brush("#88DCE8FA"),
                    Cursor = Cursors.SizeWE,
                    ToolTip = clip.Track == TimelineEngine.MainTrack
                        ? "Ajustar pela esquerda: preserva o clipe anterior e reposiciona os seguintes"
                        : "Arraste para ajustar o início do clipe"
                });
                content.Children.Add(new Border
                {
                    Width = 7,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Margin = new Thickness(0, 5, 2, 5),
                    CornerRadius = new CornerRadius(2),
                    Background = Brush("#88DCE8FA"),
                    Cursor = Cursors.SizeWE,
                    ToolTip = "Arraste para ajustar o fim do clipe"
                });
            }
            if (hasWarning)
                content.Children.Add(CreateClipWarningBadge());
            var rect = new Border {
                Width = Math.Max(8, Seconds(clip.Duration) * Scale - 2), Height = TrackHeight - 9,
                CornerRadius = new CornerRadius(4),
                Background = Brush(clip.Kind switch { MediaKind.Audio => "#347B67", MediaKind.Image => "#8058A3", _ => "#346F9B" }),
                BorderBrush = clip.Id == selectedId ? Brushes.White : hasWarning ? Brush("#F5BC53") : Brush("#708196"),
                BorderThickness = new Thickness(clip.Id == selectedId || hasWarning ? 2 : 1),
                Opacity = preview ? .72 : 1,
                ToolTip = $"{(linked?.DisplayName ?? clip.Kind.ToString())} | {clip.Association} | início {Timecode.Format(clip.Start)} | duração editada {Timecode.Format(clip.Duration)}" +
                    (linked is not null ? $"\nArquivo vinculado: {linked.SourcePath}" : "\nClipe de demonstração") +
                    (linked is { Kind: MediaKind.Audio or MediaKind.Video }
                        ? $"\nTrecho na fonte: {Timecode.Format(clip.SourceStart)} até {Timecode.Format(clip.SourceEnd)}" : "") +
                    (linked?.DurationTicks is long originalTicks
                        ? $"\nDuração original aferida: {Timecode.Format(new MediaTime(originalTicks))}"
                        : linked is { Kind: MediaKind.Audio or MediaKind.Video }
                            ? "\nDuração original não aferida — projeto legado" : "") +
                    (linked?.PixelWidth is int width && linked?.PixelHeight is int height
                        ? $"\nDimensões PNG: {width} × {height}px" : "") +
                    (missingMedia ? "\nMídia indisponível: o arquivo vinculado não foi encontrado na última verificação." : "") +
                    (clip.OrphanNotice ? "\nVínculo perdido: mova a mídia para reposicioná-la" : ""),
                Child = content
            };
            rect.Tag = clip.Id;
            rect.MouseLeftButtonDown += Clip_MouseLeftButtonDown;
            rect.MouseRightButtonDown += Clip_MouseRightButtonDown;
            Canvas.SetLeft(rect, HeaderWidth + start * Scale);
            Canvas.SetTop(rect, Y(track) + 4);
            Surface.Children.Add(rect);
        }
        var displayedResize = resizingClip && hasDragPreview && previewState is not null && draggingId is Guid resizingId
            ? previewState.Clips.FirstOrDefault(c => c.Id == resizingId) : null;
        Status.Text = $"Projeto: {projectName}{(hasUnsavedChanges ? " (não salvo)" : "")} | Cursor {Timecode.Format(MediaTime.FromSeconds((decimal)playheadSeconds))} | Versão {session.Current.Version} | {session.Current.Clips.Count} clipes | Duração {Timecode.Format(session.Current.Duration)} | " +
            (selected is not null ? Describe(selected) : "Selecione um clipe e arraste para mover.") +
            (resizingClip && hasDragPreview ? previewError is not null
                ? ""
                : $" | Prévia: início {Timecode.Format(displayedResize?.Start ?? resizePreviewStart)}, duração {Timecode.Format(displayedResize?.Duration ?? resizePreviewDuration)}" +
                  (resolvedPreviewTrack is int newTrack && newTrack != originalTrack
                      ? $" | Realocação: Trilha {newTrack + 1}" : "") : "") +
            (draggingMain && hasDragPreview ? previewError is not null
                ? ""
                : $" | Nova posição principal: {previewMainIndex + 1}" : "") +
            (!draggingMain && hasDragPreview ? previewError == "NO_FREE_TRACK"
                ? ""
                : resolvedPreviewTrack is int lane && lane != dragPreviewTrack
                    ? $" | Realocação automática: Trilha {lane + 1}" : "" : "") +
            $" | Arquivos vinculados: {mediaAssets.Count} (indisponíveis: {availableMedia.Count(p => !p.Value)})" +
            $" | Marcadores: {session.Current.MarkerItems.Count}" +
            $" | Encaixe: {(SnapToggle?.IsChecked == true ? "0,5s" : "desligado")}" +
            " | M: marcar · Shift+M: remover · Alt+←/→: marcadores · Ctrl+roda: zoom · Ctrl+←/→: clipes · Ctrl+↑/↓: trilhas · Ctrl+D: duplicar · Ctrl+B: dividir · Ctrl+Z/Y: histórico";
    }

    private static Border CreateClipWarningBadge()
    {
        // Overlay is not hit-testable: left drag and right-click continue to
        // operate on the clip underneath, including when the badge is visible.
        return new Border
        {
            Width = 19, Height = 19, CornerRadius = new CornerRadius(3),
            Background = Brush("#F5BC53"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 2, 0),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = "!", FontWeight = FontWeights.Bold, Foreground = Brush("#24202A"),
                TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    private static string Describe(Clip c) => $"Selecionado: trilha {c.Track + 1}, {Timecode.Format(c.Start)}, {c.Association}" +
        (c.OrphanNotice ? " — vínculo perdido: mova a mídia para retirar o aviso" : "");

    private static Brush Brush(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;
    private void AddText(string text, double left, double top, Brush color, double size)
    {
        var label = new TextBlock { Text = text, Foreground = color, FontSize = size, IsHitTestVisible = false };
        Canvas.SetLeft(label, left); Canvas.SetTop(label, top); Surface.Children.Add(label);
    }

    private double CursorSecondsAtX(double x) => Math.Clamp(
        Math.Round((x - HeaderWidth) / Scale, 1, MidpointRounding.AwayFromZero),
        0, Math.Max(0, (Surface.Width - HeaderWidth) / Scale));

    private void Surface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // The ruler owns marker selection and playhead scrubbing. Clicking an
        // empty clip row just positions the cursor (without editing the project).
        if (draggingId is not null) return;
        var pointer = e.GetPosition(Surface);
        if (pointer.X < HeaderWidth) return;
        if (pointer.Y < RulerHeight)
        {
            var closest = session.Current.MarkerItems
                .OrderBy(m => Math.Abs(X(m.At) - pointer.X)).FirstOrDefault();
            if (closest is not null && Math.Abs(X(closest.At) - pointer.X) <= 9)
            {
                draggingMarkerId = closest.Id;
                markerDragOriginal = closest.At;
                markerDragPreview = closest.At;
                preDragPlayhead = playheadSeconds;
                playheadSeconds = Seconds(closest.At);
            }
            else
            {
                scrubbingRuler = true;
                playheadSeconds = CursorSecondsAtX(pointer.X);
            }
            Surface.CaptureMouse();
        }
        else playheadSeconds = CursorSecondsAtX(pointer.X);
        Draw();
        e.Handled = true;
    }

    private void AddMarker_Click(object sender, RoutedEventArgs e)
    {
        var at = MediaTime.FromSeconds((decimal)Math.Round(playheadSeconds, 1));
        if (session.Current.MarkerItems.Any(m => m.At == at))
            return;
        var taken = session.Current.MarkerItems.Select(m => m.Label).ToHashSet();
        var number = 1;
        while (taken.Contains($"Marcador {number}")) number++;
        var marker = new TimelineMarker(Guid.NewGuid(), at, $"Marcador {number}");
        Run(s => TimelineEngine.AddMarker(s, s.Version, marker));
    }

    private void RemoveMarker_Click(object sender, RoutedEventArgs e)
    {
        var at = MediaTime.FromSeconds((decimal)Math.Round(playheadSeconds, 1));
        var nearest = session.Current.MarkerItems
            .Where(m => Math.Abs(m.At.Ticks - at.Ticks) <= 1_000_000) // 0.1s tolerance
            .OrderBy(m => Math.Abs(m.At.Ticks - at.Ticks)).FirstOrDefault();
        if (nearest is null) return;
        Run(s => TimelineEngine.RemoveMarker(s, s.Version, nearest.Id));
    }

    private void NavigateMarker(Key direction)
    {
        var current = MediaTime.FromSeconds((decimal)Math.Round(playheadSeconds, 1));
        var next = direction == Key.Right
            ? session.Current.MarkerItems.Where(m => m.At.Ticks > current.Ticks)
                .OrderBy(m => m.At.Ticks).FirstOrDefault()
            : session.Current.MarkerItems.Where(m => m.At.Ticks < current.Ticks)
                .OrderByDescending(m => m.At.Ticks).FirstOrDefault();
        if (next is null) return;
        playheadSeconds = Seconds(next.At);
        Draw();
        ScrollCursorIntoView();
    }

    private void ScrollCursorIntoView()
    {
        var x = HeaderWidth + playheadSeconds * Scale;
        if (x < Viewport.HorizontalOffset || x > Viewport.HorizontalOffset + Viewport.ViewportWidth)
            Viewport.ScrollToHorizontalOffset(Math.Max(0, x - Viewport.ViewportWidth / 2));
    }

    private void StepCursor(double seconds)
    {
        playheadSeconds = Math.Clamp(Math.Round(playheadSeconds + seconds, 2), 0,
            Math.Max(0, Seconds(session.Current.Duration)));
        Draw();
        ScrollCursorIntoView();
    }


    private void NavigateSelectedClip(Key direction)
    {
        // Left/right follow the visual row first, then continue to an adjacent
        // occupied row at the boundary. Up/down prioritize horizontal alignment.
        var navigation = direction switch
        {
            Key.Left => NavigationDirection.Left,
            Key.Right => NavigationDirection.Right,
            Key.Up => NavigationDirection.Up,
            Key.Down => NavigationDirection.Down,
            _ => (NavigationDirection?)null
        };
        if (navigation is null) return;
        var next = TimelineNavigation.FindNeighbor(session.Current, selectedId, navigation.Value);
        if (next is null) return;
        selectedId = next.Id;
        Draw();
        Viewport.ScrollToHorizontalOffset(Math.Max(0, X(next.Start) - Viewport.ViewportWidth / 2));
        Viewport.ScrollToVerticalOffset(Math.Max(0, Y(next.Track) - Viewport.ViewportHeight / 2));
    }

    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        var clip = Selected();
        if (clip is null) return;
        if (clip.Track == TimelineEngine.MainTrack) return;
        var newId = Guid.NewGuid();
        Run(s => TimelineEngine.DuplicateSecondary(s, s.Version, clip.Id, newId));
        if (session.Current.Clips.Any(c => c.Id == newId)) selectedId = newId;
        Draw();
    }

    private void SplitAtPlayhead_Click(object sender, RoutedEventArgs e)
    {
        var clip = Selected();
        if (clip is null) return;
        var at = MediaTime.FromSeconds((decimal)playheadSeconds);
        if (at.Ticks <= clip.Start.Ticks || at.Ticks >= clip.End.Ticks)
            return;
        Run(state => TimelineEngine.Split(state, state.Version, clip.Id, at, Guid.NewGuid()));
    }

    private void GoStart_Click(object sender, RoutedEventArgs e)
    {
        playheadSeconds = 0;
        Viewport.ScrollToHorizontalOffset(0);
        Draw();
    }

    private void GoEnd_Click(object sender, RoutedEventArgs e)
    {
        playheadSeconds = Seconds(session.Current.Duration);
        Draw();
        Viewport.ScrollToHorizontalOffset(Math.Max(0, HeaderWidth + playheadSeconds * Scale - Viewport.ViewportWidth / 2));
    }

    private void FitZoom_Click(object sender, RoutedEventArgs e)
    {
        var rightmost = session.Current.MarkerItems.Count == 0 ? 0 :
            session.Current.MarkerItems.Max(m => Seconds(m.At));
        var seconds = Math.Max(1, Math.Max(Seconds(session.Current.Duration), rightmost) + 2);
        var available = Math.Max(200, Viewport.ViewportWidth - HeaderWidth - 30);
        ZoomSlider.Value = Math.Clamp(available / seconds, ZoomSlider.Minimum, ZoomSlider.Maximum);
        Viewport.ScrollToHorizontalOffset(0);
        Draw();
    }

    private void Clip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border b || b.Tag is not Guid id) return;
        selectedId = id;
        var clip = session.Current.Find(id);
        draggingId = id;
        // Detect either edge before redrawing the clip. The middle remains a drag target.
        var local = e.GetPosition(b);
        resizingLeftEdge = b.ActualWidth >= 28 && local.X <= 11;
        var resizingRightEdge = b.ActualWidth >= 28 && local.X >= b.ActualWidth - 11;
        resizingClip = resizingLeftEdge || resizingRightEdge;
        originalResizeStart = clip.Start;
        resizePreviewStart = clip.Start;
        originalResizeDuration = clip.Duration;
        resizePreviewDuration = clip.Duration;
        var pointer = e.GetPosition(Surface);
        dragStartX = pointer.X;
        dragStartY = pointer.Y;
        originalSeconds = Seconds(clip.Start);
        originalTrack = clip.Track;
        dragPreviewSeconds = originalSeconds;
        dragPreviewTrack = originalTrack;
        draggingMain = clip.Track == TimelineEngine.MainTrack && !resizingClip;
        originalMainIndex = draggingMain
            ? session.Current.Clips.Where(c => c.Track == TimelineEngine.MainTrack)
                .OrderBy(c => c.Start.Ticks).Select(c => c.Id).ToList().IndexOf(id)
            : -1;
        previewMainIndex = originalMainIndex;
        previewState = null;
        previewError = null;
        resolvedPreviewTrack = null;
        hasDragPreview = false;
        Surface.CaptureMouse();
        Draw();
        e.Handled = true;
    }

    private void Clip_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (draggingId is not null || sender is not Border b || b.Tag is not Guid id) return;
        // Right-click opens the clip menu. Left-click only selects or starts a drag.
        // Mark handled so the Canvas does not treat this gesture as another action.
        selectedId = id;
        // Keep the clicked visual alive until the popup closes. Rebuilding the
        // Canvas here previously detached the Border under the mouse before
        // the context menu could be displayed reliably.
        ShowClipMenu(id, b);
        e.Handled = true;
    }

    private void Surface_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && (scrubbingRuler || draggingMarkerId is not null))
        {
            var at = CursorSecondsAtX(e.GetPosition(Surface).X);
            if (scrubbingRuler) playheadSeconds = at;
            else
            {
                var snapped = SnapToggle?.IsChecked == true
                    ? Math.Round(at / SnapStepSeconds, MidpointRounding.AwayFromZero) * SnapStepSeconds
                    : at;
                markerDragPreview = MediaTime.FromSeconds((decimal)snapped);
                playheadSeconds = snapped;
            }
            Draw();
            return;
        }
        if (draggingId is null || e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(Surface);
        if (Math.Abs(p.X - dragStartX) < 4 && Math.Abs(p.Y - dragStartY) < 4 && !hasDragPreview) return;
        hasDragPreview = true;
        if (resizingClip)
        {
            var clip = session.Current.Find(draggingId.Value);
            var rawBoundary = (resizingLeftEdge ? originalSeconds
                : originalSeconds + Seconds(originalResizeDuration)) + (p.X - dragStartX) / Scale;
            var snapped = SnapToggle?.IsChecked == true
                ? Math.Round(rawBoundary / SnapStepSeconds, MidpointRounding.AwayFromZero) * SnapStepSeconds
                : Math.Round(rawBoundary * 10, MidpointRounding.AwayFromZero) / 10;
            if (resizingLeftEdge)
            {
                // For main clips this is a virtual source-edge position, NOT
                // a shared boundary: the previous clip must never change size.
                // The selected clip retains its actual start, while its duration
                // changes and the following clips ripple to the new positions.
                // The first main clip may extend its *virtual* source edge
                // before timeline zero to recover previously trimmed footage.
                // Its real timeline start stays at zero. Other left edges are
                // limited by the source offset and never go before timeline zero.
                var earliest = clip.Track == TimelineEngine.MainTrack &&
                    clip.Start == MediaTime.Zero
                    ? -Seconds(clip.SourceStart)
                    : Math.Max(0.0, originalSeconds - Seconds(clip.SourceStart));
                var newStart = Math.Clamp(snapped, earliest,
                    originalSeconds + Seconds(originalResizeDuration) - .1);
                // Clamp first, then preserve exact source-time precision. A
                // second 0.1-second rounding could cross the measured boundary.
                resizePreviewStart = MediaTime.FromSeconds((decimal)newStart);
                resizePreviewDuration = clip.End - resizePreviewStart;
            }
            else
            {
                // The handle visibly stops at the *actual* last instant of
                // audio/video rather than showing an invalid preview or popup.
                var wanted = Math.Round((snapped - originalSeconds) * 10,
                    MidpointRounding.AwayFromZero) / 10;
                var source = session.Media.FirstOrDefault(m => m.Id == clip.MediaId);
                var remainingSeconds = source?.DurationTicks is long fullTicks &&
                    clip.Kind is (MediaKind.Audio or MediaKind.Video)
                    ? Math.Max(0, (fullTicks - clip.SourceStart.Ticks) /
                        (double)TimeSpan.TicksPerSecond)
                    : double.PositiveInfinity;
                var smallest = Math.Min(.1, remainingSeconds);
                var seconds = Math.Clamp(wanted, smallest, remainingSeconds);
                resizePreviewStart = originalResizeStart;
                resizePreviewDuration = MediaTime.FromSeconds((decimal)seconds);
            }
            try
            {
                var preview = resizingLeftEdge
                    ? clip.Track == TimelineEngine.MainTrack
                        ? TimelineEngine.PreviewTrimMainLeft(session.Current, session.Current.Version,
                            clip.Id, resizePreviewStart)
                        : TimelineEngine.PreviewTrimSecondaryLeft(session.Current, session.Current.Version,
                            clip.Id, resizePreviewStart)
                    : clip.Track == TimelineEngine.MainTrack
                        ? TimelineEngine.PreviewResizeMain(session.Current, session.Current.Version,
                            clip.Id, resizePreviewDuration)
                        : TimelineEngine.PreviewResizeSecondary(session.Current, session.Current.Version,
                            clip.Id, resizePreviewDuration);
                // Preview the same source-bound validation as the actual commit:
                // invalid over-extensions must not appear to be valid while dragging.
                // Only the resized occurrence changes its source interval.
                // Check it directly rather than scanning the entire library
                // during every pointer movement.
                var source = session.Media.FirstOrDefault(m => m.Id == clip.MediaId);
                if (source is not null)
                    MediaReferences.CheckSourceBounds(preview.State.Find(clip.Id), source);
                previewState = preview.State;
                resolvedPreviewTrack = preview.State.Find(clip.Id).Track;
                previewError = null;
            }
            catch (TimelineException ex)
            {
                previewState = null;
                resolvedPreviewTrack = null;
                previewError = ex.Code;
            }
            catch (MediaReferenceException)
            {
                previewState = null;
                resolvedPreviewTrack = null;
                previewError = "SOURCE_BOUNDS";
            }
            catch (OverflowException)
            {
                previewState = null;
                resolvedPreviewTrack = null;
                previewError = "TIME_OVERFLOW";
            }
            Draw();
            return;
        }
        if (draggingMain)
        {
            var mains = session.Current.Clips.Where(c => c.Track == TimelineEngine.MainTrack)
                .OrderBy(c => c.Start.Ticks).ToList();
            // The pointer displaces the clip's center; crossing another clip's center
            // changes its order. Main clips always remain on the main track.
            var moving = mains.Single(c => c.Id == draggingId!.Value);
            var center = Math.Max(0, originalSeconds + (p.X - dragStartX) / Scale) + Seconds(moving.Duration) / 2;
            var target = mains.Where(c => c.Id != moving.Id)
                .Count(c => Seconds(c.Start) + Seconds(c.Duration) / 2 < center);
            if (target != previewMainIndex || previewState is null)
            {
                previewMainIndex = target;
                previewError = null;
                try
                {
                    previewState = target == originalMainIndex ? session.Current
                        : TimelineEngine.ReorderMain(session.Current, session.Current.Version, moving.Id, target).State;
                }
                catch (TimelineException ex)
                {
                    previewState = null;
                    previewError = ex.Code;
                }
            }
        }
        else
        {
            var rawSeconds = Math.Max(0, originalSeconds + (p.X - dragStartX) / Scale);
            dragPreviewSeconds = SnapToggle?.IsChecked == true
                ? Math.Round(rawSeconds / SnapStepSeconds, MidpointRounding.AwayFromZero) * SnapStepSeconds
                : Math.Round(rawSeconds * 10, MidpointRounding.AwayFromZero) / 10;
            dragPreviewTrack = Math.Clamp((int)((p.Y - RulerHeight) / TrackHeight), 1, TimelineEngine.MaxTracks - 1);
            // Resolve the exact same command used on mouse-up, without committing it.
            // The ghost follows the actual lane chosen by automatic collision handling.
            try
            {
                var candidate = TimelineEngine.PreviewDragSecondary(session.Current, session.Current.Version,
                    draggingId.Value, dragPreviewTrack, MediaTime.FromSeconds((decimal)dragPreviewSeconds));
                resolvedPreviewTrack = candidate.State.Find(draggingId.Value).Track;
                previewError = null;
            }
            catch (TimelineException ex)
            {
                resolvedPreviewTrack = null;
                previewError = ex.Code;
            }
        }
        Draw();
    }

    private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control || draggingId is not null) return;
        // Zoom about the mouse pointer while keeping the slider as the only
        // visible zoom control. Regular mouse wheel still scrolls normally.
        var localX = e.GetPosition(Viewport).X;
        var oldScale = Scale;
        var newScale = Math.Clamp(oldScale + Math.Sign(e.Delta) * 5,
            ZoomSlider.Minimum, ZoomSlider.Maximum);
        var anchoredSeconds = Math.Max(0, (Viewport.HorizontalOffset + localX - HeaderWidth) / oldScale);
        ZoomSlider.Value = newScale;
        Viewport.UpdateLayout();
        Viewport.ScrollToHorizontalOffset(Math.Max(0,
            HeaderWidth + anchoredSeconds * newScale - localX));
        e.Handled = true;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Text inputs own typing, arrows, Delete and local Ctrl+Z.
        // The app-level open/new/import/save shortcuts remain accessible.
        if (Keyboard.FocusedElement is TextBox or ComboBox)
        {
            var globalCtrl = Keyboard.Modifiers == ModifierKeys.Control &&
                (e.Key is Key.S or Key.O or Key.N or Key.I);
            var globalSaveAs = Keyboard.Modifiers ==
                (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.S;
            if (!globalCtrl && !globalSaveAs) return;
        }

        if (e.Key == Key.Escape && (draggingMarkerId is not null || scrubbingRuler))
        {
            if (draggingMarkerId is not null) playheadSeconds = preDragPlayhead;
            draggingMarkerId = null;
            scrubbingRuler = false;
            Surface.ReleaseMouseCapture();
            Draw();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape && draggingId is not null)
        {
            draggingId = null;
            hasDragPreview = false;
            draggingMain = false;
            resizingClip = false;
            resizingLeftEdge = false;
            previewState = null;
            previewError = null;
            resolvedPreviewTrack = null;
            Surface.ReleaseMouseCapture();
            Draw();
            e.Handled = true;
            return;
        }
        if (draggingId is not null || draggingMarkerId is not null || scrubbingRuler) return;
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S)
        {
            SaveProject(); e.Handled = true; return;
        }
        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.S)
        {
            SaveProject(choosePath: true); e.Handled = true; return;
        }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.O)
        {
            OpenProject_Click(this, new RoutedEventArgs()); e.Handled = true; return;
        }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.I)
        {
            ImportMedia_Click(this, new RoutedEventArgs()); e.Handled = true; return;
        }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.N)
        {
            NewProject_Click(this, new RoutedEventArgs()); e.Handled = true; return;
        }
        // WPF reports Alt+arrow combinations through SystemKey.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.M)
        {
            AddMarker_Click(this, new RoutedEventArgs()); e.Handled = true; return;
        }
        if (Keyboard.Modifiers == ModifierKeys.Shift && e.Key == Key.M)
        {
            RemoveMarker_Click(this, new RoutedEventArgs()); e.Handled = true; return;
        }
        if (Keyboard.Modifiers == ModifierKeys.Alt && (key == Key.Left || key == Key.Right))
        {
            NavigateMarker(key); e.Handled = true; return;
        }
        if (Keyboard.Modifiers == ModifierKeys.Control && (e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.Up || e.Key == Key.Down))
        {
            NavigateSelectedClip(e.Key);
            e.Handled = true;
            return;
        }
        if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.Left)
        {
            StepCursor(-0.5); e.Handled = true; return;
        }
        if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.Right)
        {
            StepCursor(0.5); e.Handled = true; return;
        }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.D)
        {
            Duplicate_Click(this, new RoutedEventArgs()); e.Handled = true; return;
        }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.B)
        {
            SplitAtPlayhead_Click(this, new RoutedEventArgs()); e.Handled = true;
            return;
        }
        if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.Home)
        {
            GoStart_Click(this, new RoutedEventArgs()); e.Handled = true;
            return;
        }
        if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.End)
        {
            GoEnd_Click(this, new RoutedEventArgs()); e.Handled = true;
            return;
        }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z)
        {
            Undo_Click(this, new RoutedEventArgs()); e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Y)
        {
            Redo_Click(this, new RoutedEventArgs()); e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.Delete)
        {
            Delete_Click(this, new RoutedEventArgs()); e.Handled = true;
        }
    }

    private void Surface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (scrubbingRuler)
        {
            scrubbingRuler = false;
            playheadSeconds = CursorSecondsAtX(e.GetPosition(Surface).X);
            Surface.ReleaseMouseCapture();
            Draw();
            return;
        }
        if (draggingMarkerId is Guid markerId)
        {
            var movedTo = markerDragPreview;
            var movedFrom = markerDragOriginal;
            draggingMarkerId = null;
            Surface.ReleaseMouseCapture();
            if (movedTo != movedFrom)
            {
                // No history entry for clicking an existing marker; failed
                // collisions preserve the original marker and all other data.
                if (session.Current.MarkerItems.Any(m => m.Id != markerId && m.At == movedTo))
                {
                    playheadSeconds = Seconds(movedFrom);
                }
                else Run(s => TimelineEngine.MoveMarker(s, s.Version, markerId, movedTo));
            }
            Draw();
            return;
        }
        FinishDrag();
    }
    private void Surface_MouseLeave(object sender, MouseEventArgs e)
    {
        // Mouse capture preserves the gesture outside the surface; mouse-up commits it.
    }
    private void FinishDrag()
    {
        var id = draggingId;
        var resize = resizingClip;
        var finalResizeDuration = resizePreviewDuration;
        var finalResizeStart = resizePreviewStart;
        var leftEdge = resizingLeftEdge;
        var commit = hasDragPreview && id is not null;
        var main = draggingMain;
        var target = previewMainIndex;
        var valid = previewError is null;
        var seconds = dragPreviewSeconds;
        var track = dragPreviewTrack;
        draggingId = null;
        hasDragPreview = false;
        draggingMain = false;
        resizingClip = false;
        resizingLeftEdge = false;
        previewState = null;
        previewError = null;
        resolvedPreviewTrack = null;
        Surface.ReleaseMouseCapture();
        if (commit && id is Guid clipId)
        {
            if (resize && valid && (leftEdge
                ? finalResizeStart != originalResizeStart
                : finalResizeDuration != originalResizeDuration))
            {
                // One command per gesture: release commits a single undo step.
                // Failed resize leaves both timeline and history unchanged.
                Run(s => leftEdge
                    ? s.Find(clipId).Track == TimelineEngine.MainTrack
                        ? TimelineEngine.TrimMainLeft(s, s.Version, clipId, finalResizeStart)
                        : TimelineEngine.TrimSecondaryLeft(s, s.Version, clipId, finalResizeStart)
                    : s.Find(clipId).Track == TimelineEngine.MainTrack
                        ? TimelineEngine.ResizeMain(s, s.Version, clipId, finalResizeDuration)
                        : TimelineEngine.ResizeSecondary(s, s.Version, clipId, finalResizeDuration));
            }
            else if (main && valid && target != originalMainIndex)
                Run(s => TimelineEngine.ReorderMain(s, s.Version, clipId, target));
            else if (!main && (Math.Abs(seconds - originalSeconds) > 0.00001 || track != originalTrack))
                Run(s => TimelineEngine.DragSecondary(s, s.Version, clipId, track, MediaTime.FromSeconds((decimal)seconds)));
        }
        Draw();
    }

    private void ShowClipMenu(Guid clipId, Border clickedClip)
    {
        var clip = session.Current.Clips.FirstOrDefault(c => c.Id == clipId);
        if (clip is null) return;
        selectedId = clipId;
        var menu = new ContextMenu { PlacementTarget = clickedClip,
            Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };
        menu.Closed += (_, _) => Draw();
        var duplicate = MakeClipMenuItem("Duplicar", "copy", Duplicate_Click);
        duplicate.IsEnabled = clip.Track != TimelineEngine.MainTrack;
        menu.Items.Add(duplicate);
        menu.Items.Add(MakeClipMenuItem("Excluir", "trash", Delete_Click));
        var independent = MakeClipMenuItem("Tornar independente", "unlink", Independent_Click);
        independent.IsEnabled = clip.Track != TimelineEngine.MainTrack && clip.Association == AssociationKind.Positioned;
        menu.Items.Add(independent);
        var relink = MakeClipMenuItem("Relocalizar mídia...", "folder", RelinkSelectedMedia_Click);
        relink.IsEnabled = mediaAssets.Any(m => m.Id == clip.MediaId);
        menu.Items.Add(new Separator());
        var restore = MakeClipMenuItem("Restaurar duração original do clipe", "clock", FitSelectedClip_Click);
        restore.IsEnabled = clip.Kind is MediaKind.Video or MediaKind.Audio &&
            mediaAssets.Any(m => m.Id == clip.MediaId);
        restore.ToolTip = "Recupera todo o áudio/vídeo original e zera os cortes; Ctrl+Z pode desfazer.";
        menu.Items.Add(restore);
        menu.Items.Add(relink);
        menu.IsOpen = true;
    }

    private static readonly Dictionary<string, string> MenuIconPaths = new()
    {
        ["clock"] = "M896 128C472 128 128 472 128 896s344 768 768 768 768-344 768-768S1320 128 896 128zm0 1408c-353 0-640-287-640-640s287-640 640-640 640 287 640 640-287 640-640 640zm64-1024H832v448l352 211 64-105-288-170V512z",
        ["folder"] = "M1600 512H800L640 352H192Q128 352 128 416V1376Q128 1440 192 1440H1600Q1664 1440 1664 1376V576Q1664 512 1600 512Z",
        ["copy"] = "M1696 1152q40 0 68 -28t28 -68v-1216q0 -40 -28 -68t-68 -28h-960q-40 0 -68 28t-28 68v288h-544q-40 0 -68 28t-28 68v672q0 40 20 88t48 76l408 408q28 28 76 48t88 20h416q40 0 68 -28t28 -68v-328q68 40 128 40h416zM1152 939l-299 -299h299v299zM512 1323l-299 -299 h299v299zM708 676l316 316v416h-384v-416q0 -40 -28 -68t-68 -28h-416v-640h512v256q0 40 20 88t48 76zM1664 -128v1152h-384v-416q0 -40 -28 -68t-68 -28h-416v-640h896z",
        ["trash"] = "M512 800v-576q0 -14 -9 -23t-23 -9h-64q-14 0 -23 9t-9 23v576q0 14 9 23t23 9h64q14 0 23 -9t9 -23zM768 800v-576q0 -14 -9 -23t-23 -9h-64q-14 0 -23 9t-9 23v576q0 14 9 23t23 9h64q14 0 23 -9t9 -23zM1024 800v-576q0 -14 -9 -23t-23 -9h-64q-14 0 -23 9t-9 23v576 q0 14 9 23t23 9h64q14 0 23 -9t9 -23zM1152 76v948h-896v-948q0 -22 7 -40.5t14.5 -27t10.5 -8.5h832q3 0 10.5 8.5t14.5 27t7 40.5zM480 1152h448l-48 117q-7 9 -17 11h-317q-10 -2 -17 -11zM1408 1120v-64q0 -14 -9 -23t-23 -9h-96v-948q0 -83 -47 -143.5t-113 -60.5h-832 q-66 0 -113 58.5t-47 141.5v952h-96q-14 0 -23 9t-9 23v64q0 14 9 23t23 9h309l70 167q15 37 54 63t79 26h320q40 0 79 -26t54 -63l70 -167h309q14 0 23 -9t9 -23z",
        ["unlink"] = "M439 265l-256 -256q-11 -9 -23 -9t-23 9q-9 10 -9 23t9 23l256 256q10 9 23 9t23 -9q9 -10 9 -23t-9 -23zM608 224v-320q0 -14 -9 -23t-23 -9t-23 9t-9 23v320q0 14 9 23t23 9t23 -9t9 -23zM384 448q0 -14 -9 -23t-23 -9h-320q-14 0 -23 9t-9 23t9 23t23 9h320 q14 0 23 -9t9 -23zM1648 320q0 -120 -85 -203l-147 -146q-83 -83 -203 -83q-121 0 -204 85l-334 335q-21 21 -42 56l239 18l273 -274q27 -27 68 -27.5t68 26.5l147 146q28 28 28 67q0 40 -28 68l-274 275l18 239q35 -21 56 -42l336 -336q84 -86 84 -204zM1031 1044l-239 -18 l-273 274q-28 28 -68 28q-39 0 -68 -27l-147 -146q-28 -28 -28 -67q0 -40 28 -68l274 -274l-18 -240q-35 21 -56 42l-336 336q-84 86 -84 204q0 120 85 203l147 146q83 83 203 83q121 0 204 -85l334 -335q21 -21 42 -56zM1664 960q0 -14 -9 -23t-23 -9h-320q-14 0 -23 9 t-9 23t9 23t23 9h320q14 0 23 -9t9 -23zM1120 1504v-320q0 -14 -9 -23t-23 -9t-23 9t-9 23v320q0 14 9 23t23 9t23 -9t9 -23zM1527 1353l-256 -256q-11 -9 -23 -9t-23 9q-9 10 -9 23t9 23l256 256q10 9 23 9t23 -9q9 -10 9 -23t-9 -23z",
    };

    private static Viewbox CreateClipActionIcon(string glyph, double size)
    {
        var iconPath = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(MenuIconPaths[glyph]),
            Fill = Brushes.White,
            RenderTransform = new MatrixTransform(1, 0, 0, -1, 0, 1536)
        };
        var iconCanvas = new Canvas { Width = 1792, Height = 1792 };
        iconCanvas.Children.Add(iconPath);
        return new Viewbox { Width = size, Height = size, Child = iconCanvas };
    }

    private Button CreateClipToolbarButton(string title, string glyph, string shortcut,
        RoutedEventHandler action)
    {
        var button = new Button
        {
            Content = CreateClipActionIcon(glyph, 18),
            ToolTip = $"{title}{shortcut}",
            MinWidth = 38,
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 7, 6),
            IsEnabled = false
        };
        System.Windows.Automation.AutomationProperties.SetName(button, title);
        button.Click += action;
        ClipActionsToolbar.Children.Add(button);
        return button;
    }

    private void InitializeClipActionsToolbar()
    {
        // Mesmas ações do menu de contexto (clique direito), também disponíveis na barra superior.
        duplicateToolbarButton = CreateClipToolbarButton("Duplicar", "copy", " (Ctrl+D)", Duplicate_Click);
        deleteToolbarButton = CreateClipToolbarButton("Excluir", "trash", " (Delete)", Delete_Click);
        independentToolbarButton = CreateClipToolbarButton("Tornar independente", "unlink", "", Independent_Click);
    }

    private MenuItem MakeClipMenuItem(string title, string glyph, RoutedEventHandler action)
    {
        var item = new MenuItem { Header = title, Icon = CreateClipActionIcon(glyph, 15) };
        item.Click += action;
        return item;
    }

    private void Run(Func<TimelineState, CommandResult> operation)
    {
        try { session.Execute(operation); UpdateDirty(); }
        catch (Exception ex) when (ex is MediaReferenceException or TimelineException or OverflowException)
        {
            // Timeline editing is non-interrupting: invalid operations are
            // rejected atomically, without dialog boxes or undo entries.
            System.Diagnostics.Debug.WriteLine($"Edição não aplicada: {ex}");
        }
        Draw();
    }
    private Clip? Selected() => selectedId is Guid id ? session.Current.Clips.FirstOrDefault(c => c.Id == id) : null;
    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (!session.CanUndo) return;
        session.Undo();
        mediaAssets = session.Media.ToList();
        RefreshMediaPresence();
        UpdateDirty();
        if (selectedId is Guid id && session.Current.Clips.All(c => c.Id != id)) selectedId = null;
        Draw();
    }

    private void Redo_Click(object sender, RoutedEventArgs e)
    {
        if (!session.CanRedo) return;
        session.Redo();
        mediaAssets = session.Media.ToList();
        RefreshMediaPresence();
        UpdateDirty();
        if (selectedId is Guid id && session.Current.Clips.All(c => c.Id != id)) selectedId = null;
        Draw();
    }
    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var c = Selected(); if (c is null) return;
        Run(s => c.Track == 0 ? TimelineEngine.DeleteMain(s, s.Version, c.Id) : TimelineEngine.DeleteSecondary(s, s.Version, c.Id));
        if (session.Current.Clips.All(x => x.Id != c.Id)) selectedId = null;
        Draw();
    }
    private void Independent_Click(object sender, RoutedEventArgs e)
    {
        var c = Selected(); if (c is null) return;
        Run(s => TimelineEngine.SetIndependent(s, s.Version, c.Id));
    }
    private void Snap_Changed(object sender, RoutedEventArgs e) { if (IsLoaded) Draw(); }
    private void Zoom_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { if (IsLoaded) Draw(); }
}
