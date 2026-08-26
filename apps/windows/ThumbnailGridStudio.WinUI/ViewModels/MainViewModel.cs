using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using ThumbnailGridStudio.WinUI.Helpers;
using ThumbnailGridStudio.WinUI.Models;
using ThumbnailGridStudio.WinUI.Services;

namespace ThumbnailGridStudio.WinUI.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private const int MaxConcurrentImports = 4;
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".m4v", ".avi", ".mkv", ".webm"
    };

    private readonly DispatcherQueue? _dispatcherQueue;
    private bool _isWorking;
    private int _completed;
    private int _total;
    private string _lastError = string.Empty;
    private VideoItem? _selectedVideo;
    private BitmapImage? _previewImage;
    private CancellationTokenSource? _previewCts;
    private CancellationTokenSource? _settingsSaveCts;
    private readonly string _previewDirectory;
    private bool _isLoadingSettings;
    private string? _placeholderPreviewKey;

    public MainViewModel()
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _previewDirectory = Path.Combine(Path.GetTempPath(), "thumbnail-grid-studio-preview");
        Directory.CreateDirectory(_previewDirectory);

        Videos.CollectionChanged += (_, _) => NotifyUiStateChanged();
        Settings.PropertyChanged += (_, e) =>
        {
            if (_isLoadingSettings)
            {
                return;
            }

            if (IsDerivedSettingProperty(e.PropertyName))
            {
                return;
            }

            if (IsPreviewSettingProperty(e.PropertyName))
            {
                TriggerPreviewRefresh();
            }

            QueueSettingsSave();
        };
        _ = LoadSettingsAsync();
    }

    public ObservableCollection<VideoItem> Videos { get; } = [];
    public AppSettings Settings { get; } = new();

    public VideoItem? SelectedVideo
    {
        get => _selectedVideo;
        set
        {
            if (SetProperty(ref _selectedVideo, value))
            {
                NotifyUiStateChanged();
                NotifySelectedVideoChanged();
                TriggerPreviewRefresh();
            }
        }
    }

    public bool IsWorking
    {
        get => _isWorking;
        private set
        {
            if (SetProperty(ref _isWorking, value))
            {
                NotifyUiStateChanged();
            }
        }
    }

    public double ProgressPercent => _total <= 0 ? 0 : (_completed / (double)_total) * 100d;
    public string ProgressText => _total <= 0 ? string.Empty : $"{_completed}/{_total}";

    public string LastError
    {
        get => _lastError;
        private set => SetProperty(ref _lastError, value);
    }

    public BitmapImage? PreviewImage
    {
        get => _previewImage;
        private set
        {
            if (SetProperty(ref _previewImage, value))
            {
                if (value is null)
                {
                    _placeholderPreviewKey = null;
                }

                OnPropertyChanged(nameof(HasPreview));
            }
        }
    }

    public bool HasPreview => PreviewImage is not null;
    public string SelectedTitle => SelectedVideo?.FileName ?? Localizer.Get("View.PreviewFallbackTitle", "Vorschau");
    public string SelectedDuration => SelectedVideo?.DurationText ?? "00:00";
    public string SelectedFileSize => SelectedVideo?.FileSizeText ?? "0 KB";
    public string SelectedResolution => SelectedVideo?.ResolutionText ?? "0 x 0 px";

    public bool CanAdd => !IsWorking;
    public bool CanRemove => SelectedVideo is not null && !IsWorking;
    public bool CanClear => Videos.Count > 0 && !IsWorking;
    public bool CanExport => Videos.Count > 0 && !IsWorking;
    public string DefaultOutputDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        "ThumbnailGridStudio",
        "Exports");

    public async Task AddVideosAsync(IEnumerable<string> filePaths)
    {
        LastError = string.Empty;
        IsWorking = true;

        try
        {
            var tools = FfmpegService.ResolveTools();
            var processor = new VideoProcessingService(tools);

            var existing = new HashSet<string>(Videos.Select(v => v.FilePath), StringComparer.OrdinalIgnoreCase);
            var candidates = filePaths
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(path => !existing.Contains(path))
                .ToList();

            var unsupportedExtensionCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var inputs = new List<string>(candidates.Count);
            foreach (var path in candidates)
            {
                if (IsSupportedVideo(path))
                {
                    inputs.Add(path);
                    continue;
                }

                AddExtensionCount(unsupportedExtensionCounts, path);
            }

            _completed = 0;
            _total = inputs.Count;
            NotifyProgress();

            var indexedInputs = inputs.Select((path, index) => (path, index)).ToList();
            var importResults = await RollingTaskPool.MapAsync(
                indexedInputs,
                MaxConcurrentImports,
                async (entry, cancellationToken) =>
                {
                    try
                    {
                        var metadata = await processor.LoadMetadataAsync(entry.path, cancellationToken).ConfigureAwait(false);
                        return new ImportResult(entry.path, metadata, null, false);
                    }
                    catch (Exception ex)
                    {
                        var unsupported = IsUnsupportedImportError(ex.Message);
                        return new ImportResult(entry.path, null, ex.Message, unsupported);
                    }
                },
                _ =>
                {
                    Interlocked.Increment(ref _completed);
                    RunOnUi(NotifyProgress);
                });

            foreach (var result in importResults)
            {
                if (result.Metadata is not null)
                {
                    var item = new VideoItem
                    {
                        FilePath = result.Path,
                        FileName = Path.GetFileName(result.Path),
                        Duration = result.Metadata.Duration,
                        FileSizeBytes = result.Metadata.FileSizeBytes,
                        Width = result.Metadata.Width,
                        Height = result.Metadata.Height,
                        BitrateBitsPerSecond = result.Metadata.BitrateBitsPerSecond,
                        VideoCodec = result.Metadata.VideoCodec,
                        AudioCodecs = result.Metadata.AudioCodecs ?? Array.Empty<string>()
                    };

                    Videos.Add(item);
                    SelectedVideo ??= item;
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(result.Error))
                {
                    if (result.IsUnsupported)
                    {
                        AddExtensionCount(unsupportedExtensionCounts, result.Path);
                    }
                    else
                    {
                        LastError = string.Format(
                            CultureInfo.CurrentCulture,
                            Localizer.Get("View.Error.ImportFailed", "Import fehlgeschlagen ({0}): {1}"),
                            Path.GetFileName(result.Path),
                            result.Error);
                    }
                }
            }

            if (unsupportedExtensionCounts.Count > 0)
            {
                var summary = FormatUnsupportedExtensionSummary(unsupportedExtensionCounts);
                LastError = string.Format(
                    CultureInfo.CurrentCulture,
                    Localizer.Get(
                        "View.Error.UnsupportedExtensions",
                        "Folgende Dateiendungen wurden wegen fehlender Unterstützung nicht importiert: {0}"),
                    summary);
            }
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
        finally
        {
            IsWorking = false;
        }
    }

    public async Task RenderAllAsync(string outputDirectory)
    {
        LastError = string.Empty;
        if (Videos.Count == 0)
        {
            return;
        }

        Directory.CreateDirectory(outputDirectory);
        IsWorking = true;

        try
        {
            var tools = FfmpegService.ResolveTools();
            var processor = new VideoProcessingService(tools);
            var snapshot = Videos.ToList();
            var renderSettings = Settings.CreateRenderSnapshot();

            _completed = 0;
            _total = snapshot.Count;
            NotifyProgress();

            foreach (var item in snapshot)
            {
                item.StatusText = Localizer.Get("View.Status.Waiting", "Wartet...");
            }

            await RollingTaskPool.MapAsync(
                snapshot,
                renderSettings.RenderConcurrency,
                async (item, cancellationToken) =>
                {
                    await RenderSingleAsync(
                        item,
                        outputDirectory,
                        processor,
                        renderSettings,
                        cancellationToken).ConfigureAwait(false);
                    return true;
                });
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
        finally
        {
            IsWorking = false;
        }
    }

    public void RemoveSelected()
    {
        if (SelectedVideo is null)
        {
            return;
        }

        var index = Videos.IndexOf(SelectedVideo);
        if (index < 0)
        {
            return;
        }

        Videos.RemoveAt(index);
        if (Videos.Count == 0)
        {
            SelectedVideo = null;
            PreviewImage = null;
            return;
        }

        SelectedVideo = Videos[Math.Clamp(index, 0, Videos.Count - 1)];
    }

    public void ClearAll()
    {
        Videos.Clear();
        SelectedVideo = null;
        PreviewImage = null;
        LastError = string.Empty;
        _completed = 0;
        _total = 0;
        NotifyProgress();
    }

    public void ResetRenderedOutputs(bool resetSelectionToStartupPlaceholder = false)
    {
        foreach (var item in Videos)
        {
            item.OutputPath = null;
            item.StatusText = Localizer.Get("View.Status.Ready", "Bereit");
        }

        if (resetSelectionToStartupPlaceholder && SelectedVideo is not null)
        {
            SelectedVideo = null;
            return;
        }

        TriggerPreviewRefresh();
    }

    private async Task RenderSingleAsync(
        VideoItem item,
        string outputDirectory,
        VideoProcessingService processor,
        AppSettings renderSettings,
        CancellationToken cancellationToken)
    {
        RunOnUi(() => item.StatusText = Localizer.Get("View.Status.Rendering", "Render läuft..."));

        List<ThumbnailFrame>? thumbnails = null;
        string? separateThumbnailDirectory = null;
        string? output = null;
        var outputWasCreated = false;
        try
        {
            var thumbSize = renderSettings.ResolveThumbnailSize(item.Width, item.Height);
            Func<int, ThumbnailFrame, CancellationToken, Task>? fullResolutionFrameHandler = null;

            if (renderSettings.ExportSeparateThumbnails)
            {
                var folder = OutputPathResolver.GetUniqueDirectoryPath(Path.Combine(
                    outputDirectory,
                    Path.GetFileNameWithoutExtension(item.FileName)));
                Directory.CreateDirectory(folder);
                separateThumbnailDirectory = folder;
                fullResolutionFrameHandler = (index, frame, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    ExportSeparateThumbnail(index, frame, folder, renderSettings);
                    return Task.CompletedTask;
                };
            }

            thumbnails = (await processor.GenerateThumbnailsAsync(
                item.FilePath,
                renderSettings.Columns * renderSettings.Rows,
                thumbSize.Width,
                thumbSize.Height,
                item.Duration,
                fullResolutionFrameHandler,
                cancellationToken).ConfigureAwait(false)).ToList();

            cancellationToken.ThrowIfCancellationRequested();
            var metadata = new VideoMetadata(
                item.Duration,
                item.Width,
                item.Height,
                item.FileSizeBytes,
                item.BitrateBitsPerSecond,
                item.VideoCodec,
                item.AudioCodecs);
            output = OutputPathResolver.GetUniqueFilePath(Path.Combine(
                outputDirectory,
                $"{Path.GetFileNameWithoutExtension(item.FileName)}.{renderSettings.ExportFileExtension}"));

            ContactSheetRenderer.RenderAndSave(metadata, item.FileName, thumbnails, renderSettings, output);
            outputWasCreated = true;

            RunOnUi(() =>
            {
                item.OutputPath = output;
                item.StatusText = string.Format(
                    CultureInfo.CurrentCulture,
                    Localizer.Get("View.Status.Exported", "Exportiert: {0}"),
                    Path.GetFileName(output));
                if (SelectedVideo?.FilePath.Equals(item.FilePath, StringComparison.OrdinalIgnoreCase) == true)
                {
                    _placeholderPreviewKey = null;
                    PreviewImage = new BitmapImage(new Uri(output));
                }
            });
        }
        catch (Exception ex)
        {
            if (outputWasCreated && !string.IsNullOrWhiteSpace(output))
            {
                try
                {
                    File.Delete(output);
                }
                catch
                {
                    // Keep the original rendering error.
                }
            }

            if (!string.IsNullOrWhiteSpace(separateThumbnailDirectory))
            {
                try
                {
                    Directory.Delete(separateThumbnailDirectory, recursive: true);
                }
                catch
                {
                    // Keep the original rendering error.
                }
            }

            RunOnUi(() =>
            {
                item.StatusText = string.Format(
                    CultureInfo.CurrentCulture,
                    Localizer.Get("View.Status.Error", "Fehler: {0}"),
                    ex.Message);
                LastError = string.Format(
                    CultureInfo.CurrentCulture,
                    Localizer.Get("View.Error.ExportFailed", "Export fehlgeschlagen ({0}): {1}"),
                    item.FileName,
                    ex.Message);
            });
        }
        finally
        {
            if (thumbnails is not null)
            {
                foreach (var thumb in thumbnails)
                {
                    thumb.Dispose();
                }
            }

            Interlocked.Increment(ref _completed);
            RunOnUi(NotifyProgress);
        }
    }

    public Task RenderAllToDefaultAsync()
    {
        var directory = DefaultOutputDirectory;
        Directory.CreateDirectory(directory);
        return RenderAllAsync(directory);
    }

    private void TriggerPreviewRefresh()
    {
        _previewCts?.Cancel();
        _previewCts?.Dispose();
        _previewCts = new CancellationTokenSource();
        var token = _previewCts.Token;
        _ = RefreshPreviewAsync(token);
    }

    private async Task LoadSettingsAsync()
    {
        try
        {
            _isLoadingSettings = true;
            await Settings.LoadAsync();
        }
        catch
        {
            // Ignore setting load failures.
        }
        finally
        {
            _isLoadingSettings = false;
        }

        TriggerPreviewRefresh();
    }

    private void QueueSettingsSave()
    {
        _settingsSaveCts?.Cancel();
        _settingsSaveCts?.Dispose();
        _settingsSaveCts = new CancellationTokenSource();
        var token = _settingsSaveCts.Token;
        _ = SaveSettingsDebouncedAsync(token);
    }

    private async Task SaveSettingsDebouncedAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(300, token);
            token.ThrowIfCancellationRequested();
            await Settings.SaveAsync();
        }
        catch (OperationCanceledException)
        {
            // Ignore stale save requests.
        }
        catch
        {
            // Saving settings should never break the app flow.
        }
    }

    private async Task RefreshPreviewAsync(CancellationToken cancellationToken)
    {
        if (IsWorking)
        {
            return;
        }

        try
        {
            await Task.Delay(220, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var item = SelectedVideo;
            if (item is not null && !string.IsNullOrWhiteSpace(item.OutputPath) && File.Exists(item.OutputPath))
            {
                RunOnUi(() =>
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    var image = new BitmapImage(new Uri(item.OutputPath));
                    _placeholderPreviewKey = null;
                    PreviewImage = image;
                });
                return;
            }

            var renderSettings = Settings.CreateRenderSnapshot();
            var title = item?.FileName ?? Localizer.Get("View.PreviewFallbackTitle", "Vorschau");
            var duration = item?.Duration ?? TimeSpan.Zero;
            var fileSizeBytes = item?.FileSizeBytes ?? 0;
            var width = item?.Width ?? 0;
            var height = item?.Height ?? 0;
            var bitrateBitsPerSecond = item?.BitrateBitsPerSecond ?? 0;
            var videoCodec = item?.VideoCodec ?? string.Empty;
            var audioCodecs = item?.AudioCodecs ?? Array.Empty<string>();
            var previewKey = BuildPlaceholderPreviewKey(
                title,
                duration,
                fileSizeBytes,
                width,
                height,
                bitrateBitsPerSecond,
                videoCodec,
                audioCodecs,
                renderSettings);

            if (string.Equals(previewKey, _placeholderPreviewKey, StringComparison.Ordinal) && PreviewImage is not null)
            {
                return;
            }

            await RenderPlaceholderPreviewAsync(
                title,
                duration,
                fileSizeBytes,
                width,
                height,
                bitrateBitsPerSecond,
                videoCodec,
                audioCodecs,
                renderSettings,
                previewKey,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Ignore stale preview updates.
        }
        catch
        {
            // Preview errors should not break main workflow.
        }
    }

    private async Task RenderPlaceholderPreviewAsync(
        string title,
        TimeSpan duration,
        long fileSizeBytes,
        int width,
        int height,
        long bitrateBitsPerSecond,
        string videoCodec,
        IReadOnlyList<string> audioCodecs,
        AppSettings renderSettings,
        string previewKey,
        CancellationToken cancellationToken)
    {
        var previewPath = Path.Combine(_previewDirectory, $"placeholder-{Guid.NewGuid():N}.preview.jpg");
        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            ContactSheetRenderer.RenderPlaceholderAndSave(
                title,
                duration,
                fileSizeBytes,
                width,
                height,
                bitrateBitsPerSecond,
                videoCodec,
                audioCodecs,
                renderSettings,
                previewPath);
        }, cancellationToken);

        RunOnUi(() =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            _placeholderPreviewKey = previewKey;
            PreviewImage = new BitmapImage(new Uri(previewPath));
        });
    }

    private static string BuildPlaceholderPreviewKey(
        string title,
        TimeSpan duration,
        long fileSizeBytes,
        int width,
        int height,
        long bitrateBitsPerSecond,
        string videoCodec,
        IReadOnlyList<string> audioCodecs,
        AppSettings settings)
    {
        return string.Join('|',
            title,
            duration.Ticks.ToString(CultureInfo.InvariantCulture),
            fileSizeBytes.ToString(CultureInfo.InvariantCulture),
            width.ToString(CultureInfo.InvariantCulture),
            height.ToString(CultureInfo.InvariantCulture),
            bitrateBitsPerSecond.ToString(CultureInfo.InvariantCulture),
            videoCodec,
            string.Join(';', audioCodecs),
            settings.ColumnsText,
            settings.RowsText,
            settings.ThumbnailWidthText,
            settings.ThumbnailHeightText,
            settings.SpacingText,
            settings.BackgroundHex,
            settings.MetadataHex,
            settings.FileNameFontSize.ToString("R", CultureInfo.InvariantCulture),
            settings.DurationFontSize.ToString("R", CultureInfo.InvariantCulture),
            settings.FileSizeFontSize.ToString("R", CultureInfo.InvariantCulture),
            settings.ResolutionFontSize.ToString("R", CultureInfo.InvariantCulture),
            settings.TimestampFontSize.ToString("R", CultureInfo.InvariantCulture),
            settings.BitrateFontSize.ToString("R", CultureInfo.InvariantCulture),
            settings.VideoCodecFontSize.ToString("R", CultureInfo.InvariantCulture),
            settings.AudioCodecFontSize.ToString("R", CultureInfo.InvariantCulture),
            settings.ShowFileName,
            settings.ShowDuration,
            settings.ShowFileSize,
            settings.ShowResolution,
            settings.ShowTimestamp,
            settings.ShowBitrate,
            settings.ShowVideoCodec,
            settings.ShowAudioCodec);
    }

    private static bool IsSupportedVideo(string path)
    {
        return SupportedExtensions.Contains(Path.GetExtension(path));
    }

    private static bool IsDerivedSettingProperty(string? propertyName)
    {
        return propertyName is nameof(AppSettings.Columns)
            or nameof(AppSettings.Rows)
            or nameof(AppSettings.ThumbnailWidth)
            or nameof(AppSettings.ThumbnailHeight);
    }

    private static bool IsPreviewSettingProperty(string? propertyName)
    {
        return propertyName is not nameof(AppSettings.ExportFormatIndex)
            and not nameof(AppSettings.ExportSeparateThumbnails)
            and not nameof(AppSettings.RenderConcurrency);
    }

    private static bool IsUnsupportedImportError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return false;
        }

        return error.Contains("No video stream found.", StringComparison.OrdinalIgnoreCase)
            || error.Contains("missing valid video duration", StringComparison.OrdinalIgnoreCase)
            || error.Contains("Invalid data found when processing input", StringComparison.OrdinalIgnoreCase)
            || error.Contains("could not find codec parameters", StringComparison.OrdinalIgnoreCase)
            || error.Contains("unsupported", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddExtensionCount(IDictionary<string, int> target, string path)
    {
        var ext = Path.GetExtension(path);
        var normalized = string.IsNullOrWhiteSpace(ext)
            ? Localizer.Get("View.Error.NoExtension", "(ohne Endung)")
            : ext.TrimStart('.').ToLowerInvariant();

        if (!target.TryAdd(normalized, 1))
        {
            target[normalized]++;
        }
    }

    private static string FormatUnsupportedExtensionSummary(IReadOnlyDictionary<string, int> extensionCounts)
    {
        var ordered = extensionCounts
            .OrderBy(kvp => kvp.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(kvp => string.Format(
                CultureInfo.CurrentCulture,
                Localizer.Get("View.Error.ExtensionCountItem", "{0}x {1}"),
                kvp.Value,
                kvp.Key));
        return string.Join(", ", ordered);
    }

    private void NotifyProgress()
    {
        OnPropertyChanged(nameof(ProgressPercent));
        OnPropertyChanged(nameof(ProgressText));
    }

    private void NotifyUiStateChanged()
    {
        OnPropertyChanged(nameof(CanAdd));
        OnPropertyChanged(nameof(CanRemove));
        OnPropertyChanged(nameof(CanClear));
        OnPropertyChanged(nameof(CanExport));
    }

    private void NotifySelectedVideoChanged()
    {
        OnPropertyChanged(nameof(SelectedTitle));
        OnPropertyChanged(nameof(SelectedDuration));
        OnPropertyChanged(nameof(SelectedFileSize));
        OnPropertyChanged(nameof(SelectedResolution));
    }

    private void RunOnUi(Action action)
    {
        if (_dispatcherQueue is null)
        {
            action();
            return;
        }

        if (_dispatcherQueue.HasThreadAccess)
        {
            action();
            return;
        }

        _dispatcherQueue.TryEnqueue(() => action());
    }

    private static void ExportSeparateThumbnail(
        int index,
        ThumbnailFrame thumbnail,
        string outputDirectory,
        AppSettings renderSettings)
    {
        var timestamp = FormatTimestampForFileName(thumbnail.Timestamp);
        var name = $"{index + 1:000}_{timestamp}.{renderSettings.ExportFileExtension}";
        var path = Path.Combine(outputDirectory, name);
        var format = renderSettings.ExportFormatIndex == 1
            ? System.Drawing.Imaging.ImageFormat.Png
            : System.Drawing.Imaging.ImageFormat.Jpeg;
        thumbnail.Image.Save(path, format);
    }

    private static string FormatTimestampForFileName(TimeSpan timestamp)
    {
        var totalMilliseconds = Math.Max((int)Math.Round(timestamp.TotalMilliseconds), 0);
        var hours = totalMilliseconds / 3_600_000;
        var minutes = (totalMilliseconds % 3_600_000) / 60_000;
        var seconds = (totalMilliseconds % 60_000) / 1000;
        var millis = totalMilliseconds % 1000;
        return string.Create(CultureInfo.InvariantCulture, $"{hours:00}-{minutes:00}-{seconds:00}_{millis:000}");
    }

    private sealed record ImportResult(string Path, VideoMetadata? Metadata, string? Error, bool IsUnsupported);
}
