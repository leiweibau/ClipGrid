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

    public MainViewModel()
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _previewDirectory = Path.Combine(Path.GetTempPath(), "thumbnail-grid-studio-preview");
        Directory.CreateDirectory(_previewDirectory);

        Videos.CollectionChanged += (_, _) => NotifyUiStateChanged();
        Settings.PropertyChanged += (_, _) =>
        {
            if (_isLoadingSettings)
            {
                return;
            }

            TriggerPreviewRefresh();
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
                OnPropertyChanged(nameof(HasPreview));
            }
        }
    }

    public bool HasPreview => PreviewImage is not null;
    public string SelectedTitle => SelectedVideo?.FileName ?? "Vorschau";
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
            var inputs = filePaths
                .Where(File.Exists)
                .Where(IsSupportedVideo)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(path => !existing.Contains(path))
                .ToList();

            _completed = 0;
            _total = inputs.Count;
            NotifyProgress();

            var indexedInputs = inputs.Select((path, index) => (path, index)).ToList();
            var importResults = new ImportResult[indexedInputs.Count];
            using var gate = new SemaphoreSlim(MaxConcurrentImports, MaxConcurrentImports);
            var tasks = indexedInputs.Select(async entry =>
            {
                await gate.WaitAsync();
                try
                {
                    var metadata = await processor.LoadMetadataAsync(entry.path);
                    importResults[entry.index] = new ImportResult(entry.path, metadata, null);
                }
                catch (Exception ex)
                {
                    importResults[entry.index] = new ImportResult(entry.path, null, ex.Message);
                }
                finally
                {
                    gate.Release();
                    Interlocked.Increment(ref _completed);
                    RunOnUi(NotifyProgress);
                }
            }).ToList();

            await Task.WhenAll(tasks);

            foreach (var result in importResults.Where(r => r is not null))
            {
                if (result?.Metadata is not null)
                {
                    var item = new VideoItem
                    {
                        FilePath = result.Path,
                        FileName = Path.GetFileName(result.Path),
                        Duration = result.Metadata.Duration,
                        FileSizeBytes = result.Metadata.FileSizeBytes,
                        Width = result.Metadata.Width,
                        Height = result.Metadata.Height
                    };

                    Videos.Add(item);
                    SelectedVideo ??= item;
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(result?.Error))
                {
                    LastError = $"Import fehlgeschlagen ({Path.GetFileName(result.Path)}): {result.Error}";
                }
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

            _completed = 0;
            _total = snapshot.Count;
            NotifyProgress();

            foreach (var item in snapshot)
            {
                item.StatusText = "Wartet...";
            }

            var maxConcurrency = Math.Max(Settings.RenderConcurrency, 1);
            using var gate = new SemaphoreSlim(maxConcurrency, maxConcurrency);
            var tasks = snapshot.Select(item => RenderSingleAsync(item, outputDirectory, processor, gate)).ToList();
            await Task.WhenAll(tasks);
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

    private async Task RenderSingleAsync(
        VideoItem item,
        string outputDirectory,
        VideoProcessingService processor,
        SemaphoreSlim gate)
    {
        await gate.WaitAsync();
        try
        {
            RunOnUi(() => item.StatusText = "Render läuft...");

            List<ThumbnailFrame>? thumbnails = null;
            try
            {
                var thumbSize = Settings.ResolveThumbnailSize(item.Width, item.Height);
                thumbnails = (await processor.GenerateThumbnailsAsync(
                    item.FilePath,
                    Settings.Columns * Settings.Rows,
                    thumbSize.Width,
                    thumbSize.Height,
                    item.Duration)).ToList();

                var metadata = new VideoMetadata(item.Duration, item.Width, item.Height, item.FileSizeBytes);
                var output = Path.Combine(
                    outputDirectory,
                    $"{Path.GetFileNameWithoutExtension(item.FileName)}.{Settings.ExportFileExtension}");

                ContactSheetRenderer.RenderAndSave(metadata, item.FileName, thumbnails, Settings, output);
                item.OutputPath = output;

                if (Settings.ExportSeparateThumbnails)
                {
                    var fullResolutionWidth = item.Width > 0 ? item.Width : Settings.ThumbnailWidth;
                    var fullResolutionHeight = item.Height > 0 ? item.Height : Settings.ThumbnailHeight;
                    var fullResolution = (await processor.GenerateThumbnailsAsync(
                        item.FilePath,
                        Settings.Columns * Settings.Rows,
                        fullResolutionWidth,
                        fullResolutionHeight,
                        item.Duration)).ToList();
                    try
                    {
                        ExportSeparateThumbnails(item, fullResolution, outputDirectory);
                    }
                    finally
                    {
                        foreach (var thumb in fullResolution)
                        {
                            thumb.Dispose();
                        }
                    }
                }

                RunOnUi(() => item.StatusText = $"Exportiert: {Path.GetFileName(output)}");
                if (SelectedVideo?.FilePath.Equals(item.FilePath, StringComparison.OrdinalIgnoreCase) == true)
                {
                    RunOnUi(() => PreviewImage = new BitmapImage(new Uri(output)));
                }
            }
            catch (Exception ex)
            {
                RunOnUi(() =>
                {
                    item.StatusText = $"Fehler: {ex.Message}";
                    LastError = $"Export fehlgeschlagen ({item.FileName}): {ex.Message}";
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
            }
        }
        finally
        {
            gate.Release();
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

        if (SelectedVideo is null)
        {
            await RenderPlaceholderPreviewAsync("Vorschau", TimeSpan.Zero, 0, 0, 0, cancellationToken);
            return;
        }

        try
        {
            await Task.Delay(220, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var item = SelectedVideo;
            if (item is null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(item.OutputPath) && File.Exists(item.OutputPath))
            {
                RunOnUi(() =>
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    var image = new BitmapImage(new Uri(item.OutputPath));
                    PreviewImage = image;
                });
                return;
            }

            await RenderPlaceholderPreviewAsync(
                item.FileName,
                item.Duration,
                item.FileSizeBytes,
                item.Width,
                item.Height,
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
                Settings,
                previewPath);
        }, cancellationToken);

        RunOnUi(() =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            PreviewImage = new BitmapImage(new Uri(previewPath));
        });
    }

    private static bool IsSupportedVideo(string path)
    {
        return SupportedExtensions.Contains(Path.GetExtension(path));
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

    private void ExportSeparateThumbnails(VideoItem item, IReadOnlyList<ThumbnailFrame> thumbnails, string outputDirectory)
    {
        if (thumbnails.Count == 0)
        {
            return;
        }

        var stem = Path.GetFileNameWithoutExtension(item.FileName);
        var folder = Path.Combine(outputDirectory, stem);
        Directory.CreateDirectory(folder);

        for (var i = 0; i < thumbnails.Count; i++)
        {
            var thumb = thumbnails[i];
            var timestamp = FormatTimestampForFileName(thumb.Timestamp);
            var name = $"{i + 1:000}_{timestamp}.{Settings.ExportFileExtension}";
            var path = Path.Combine(folder, name);
            var format = Settings.ExportFormatIndex == 1
                ? System.Drawing.Imaging.ImageFormat.Png
                : System.Drawing.Imaging.ImageFormat.Jpeg;
            thumb.Image.Save(path, format);
        }
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

    private sealed record ImportResult(string Path, VideoMetadata? Metadata, string? Error);
}
