using ThumbnailGridStudio.WinUI.Helpers;
using System.Text.Json;

namespace ThumbnailGridStudio.WinUI.Models;

public sealed class AppSettings : ObservableObject
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ThumbnailGridStudio");
    private static readonly string SettingsFilePath = Path.Combine(SettingsDirectory, "settings.json");

    private bool _isApplyingSnapshot;
    private string _columnsText = "4";
    private string _rowsText = "4";
    private string _thumbnailWidthText = "320";
    private string _thumbnailHeightText = "180";
    private string _spacingText = "16";
    private int _exportFormatIndex;
    private string _backgroundHex = "1F2126";
    private string _metadataHex = "FFFFFF";
    private float _fileNameFontSize = 26f;
    private float _durationFontSize = 14f;
    private float _fileSizeFontSize = 14f;
    private float _resolutionFontSize = 14f;
    private float _timestampFontSize = 12f;
    private bool _showFileName = true;
    private bool _showDuration = true;
    private bool _showFileSize = true;
    private bool _showResolution = true;
    private bool _showTimestamp = true;
    private bool _exportSeparateThumbnails;
    private int _renderConcurrency = 1;

    public string ColumnsText
    {
        get => _columnsText;
        set
        {
            if (SetProperty(ref _columnsText, value))
            {
                OnPropertyChanged(nameof(Columns));
            }
        }
    }

    public string RowsText
    {
        get => _rowsText;
        set
        {
            if (SetProperty(ref _rowsText, value))
            {
                OnPropertyChanged(nameof(Rows));
            }
        }
    }

    public string ThumbnailWidthText
    {
        get => _thumbnailWidthText;
        set
        {
            if (SetProperty(ref _thumbnailWidthText, value))
            {
                OnPropertyChanged(nameof(ThumbnailWidth));
            }
        }
    }

    public string ThumbnailHeightText
    {
        get => _thumbnailHeightText;
        set
        {
            if (SetProperty(ref _thumbnailHeightText, value))
            {
                OnPropertyChanged(nameof(ThumbnailHeight));
            }
        }
    }

    public string SpacingText
    {
        get => _spacingText;
        set
        {
            if (SetProperty(ref _spacingText, value))
            {
                OnPropertyChanged(nameof(Spacing));
            }
        }
    }

    public int ExportFormatIndex
    {
        get => _exportFormatIndex;
        set => SetProperty(ref _exportFormatIndex, value is 1 ? 1 : 0);
    }

    public string ExportFileExtension => ExportFormatIndex == 1 ? "png" : "jpg";
    public int Columns => ParseInt(ColumnsText, 4, 1, 20);
    public int Rows => ParseInt(RowsText, 4, 1, 20);
    public int ThumbnailWidth => ParseInt(ThumbnailWidthText, 320, 32, 4096);
    public int ThumbnailHeight => ParseInt(ThumbnailHeightText, 180, 32, 4096);
    public int Spacing => ParseInt(SpacingText, 16, 0, 256);
    public int? ThumbnailWidthOptional => ParseNullableInt(ThumbnailWidthText, 32, 4096);
    public int? ThumbnailHeightOptional => ParseNullableInt(ThumbnailHeightText, 32, 4096);

    public string BackgroundHex
    {
        get => _backgroundHex;
        set => SetProperty(ref _backgroundHex, NormalizeHex(value, "1F2126"));
    }

    public string MetadataHex
    {
        get => _metadataHex;
        set => SetProperty(ref _metadataHex, NormalizeHex(value, "FFFFFF"));
    }

    public float FileNameFontSize
    {
        get => _fileNameFontSize;
        set => SetProperty(ref _fileNameFontSize, Math.Clamp(value, 8f, 96f));
    }

    public float DurationFontSize
    {
        get => _durationFontSize;
        set => SetProperty(ref _durationFontSize, Math.Clamp(value, 8f, 96f));
    }

    public float FileSizeFontSize
    {
        get => _fileSizeFontSize;
        set => SetProperty(ref _fileSizeFontSize, Math.Clamp(value, 8f, 96f));
    }

    public float ResolutionFontSize
    {
        get => _resolutionFontSize;
        set => SetProperty(ref _resolutionFontSize, Math.Clamp(value, 8f, 96f));
    }

    public float TimestampFontSize
    {
        get => _timestampFontSize;
        set => SetProperty(ref _timestampFontSize, Math.Clamp(value, 8f, 96f));
    }

    public bool ShowFileName
    {
        get => _showFileName;
        set => SetProperty(ref _showFileName, value);
    }

    public bool ShowDuration
    {
        get => _showDuration;
        set => SetProperty(ref _showDuration, value);
    }

    public bool ShowFileSize
    {
        get => _showFileSize;
        set => SetProperty(ref _showFileSize, value);
    }

    public bool ShowResolution
    {
        get => _showResolution;
        set => SetProperty(ref _showResolution, value);
    }

    public bool ShowTimestamp
    {
        get => _showTimestamp;
        set => SetProperty(ref _showTimestamp, value);
    }

    public bool ExportSeparateThumbnails
    {
        get => _exportSeparateThumbnails;
        set => SetProperty(ref _exportSeparateThumbnails, value);
    }

    public int RenderConcurrency
    {
        get => _renderConcurrency;
        set => SetProperty(ref _renderConcurrency, Math.Clamp(value, 1, 8));
    }

    public async Task LoadAsync()
    {
        if (!File.Exists(SettingsFilePath))
        {
            return;
        }

        try
        {
            await using var stream = File.OpenRead(SettingsFilePath);
            var snapshot = await JsonSerializer.DeserializeAsync<AppSettingsSnapshot>(stream);
            if (snapshot is null)
            {
                return;
            }

            _isApplyingSnapshot = true;
            ApplySnapshot(snapshot);
        }
        catch
        {
            // Ignore invalid persisted settings.
        }
        finally
        {
            _isApplyingSnapshot = false;
        }
    }

    public async Task SaveAsync()
    {
        if (_isApplyingSnapshot)
        {
            return;
        }

        Directory.CreateDirectory(SettingsDirectory);
        var snapshot = CreateSnapshot();
        await using var stream = File.Create(SettingsFilePath);
        await JsonSerializer.SerializeAsync(stream, snapshot, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    private static string NormalizeHex(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var normalized = value.Trim().TrimStart('#').ToUpperInvariant();
        if (normalized.Length != 6 || !normalized.All(Uri.IsHexDigit))
        {
            return fallback;
        }

        return normalized;
    }

    private static int ParseInt(string? text, int fallback, int min, int max)
    {
        if (!int.TryParse(text, out var value))
        {
            return fallback;
        }

        return Math.Clamp(value, min, max);
    }

    private static int? ParseNullableInt(string? text, int min, int max)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (!int.TryParse(text, out var value))
        {
            return null;
        }

        return Math.Clamp(value, min, max);
    }

    public (int Width, int Height) ResolveThumbnailSize(int sourceWidth, int sourceHeight)
    {
        var width = ThumbnailWidthOptional;
        var height = ThumbnailHeightOptional;
        var aspectRatio = sourceWidth > 0 && sourceHeight > 0
            ? sourceWidth / (double)sourceHeight
            : 16d / 9d;

        return (width, height) switch
        {
            ({ } w, { } h) => (w, h),
            ({ } w, null) => (w, Math.Max((int)Math.Round(w / aspectRatio), 1)),
            (null, { } h) => (Math.Max((int)Math.Round(h * aspectRatio), 1), h),
            _ => (320, 180)
        };
    }

    // Placeholder previews should always use 16:9 for auto-calculation.
    public (int Width, int Height) ResolvePlaceholderThumbnailSize()
    {
        var width = ThumbnailWidthOptional;
        var height = ThumbnailHeightOptional;
        const double aspectRatio = 16d / 9d;

        return (width, height) switch
        {
            ({ } w, { } h) => (w, h),
            ({ } w, null) => (w, Math.Max((int)Math.Round(w / aspectRatio), 1)),
            (null, { } h) => (Math.Max((int)Math.Round(h * aspectRatio), 1), h),
            _ => (320, 180)
        };
    }

    private AppSettingsSnapshot CreateSnapshot()
    {
        return new AppSettingsSnapshot
        {
            ColumnsText = ColumnsText,
            RowsText = RowsText,
            ThumbnailWidthText = ThumbnailWidthText,
            ThumbnailHeightText = ThumbnailHeightText,
            SpacingText = SpacingText,
            ExportFormatIndex = ExportFormatIndex,
            BackgroundHex = BackgroundHex,
            MetadataHex = MetadataHex,
            FileNameFontSize = FileNameFontSize,
            DurationFontSize = DurationFontSize,
            FileSizeFontSize = FileSizeFontSize,
            ResolutionFontSize = ResolutionFontSize,
            TimestampFontSize = TimestampFontSize,
            ShowFileName = ShowFileName,
            ShowDuration = ShowDuration,
            ShowFileSize = ShowFileSize,
            ShowResolution = ShowResolution,
            ShowTimestamp = ShowTimestamp,
            ExportSeparateThumbnails = ExportSeparateThumbnails,
            RenderConcurrency = RenderConcurrency
        };
    }

    private void ApplySnapshot(AppSettingsSnapshot snapshot)
    {
        ColumnsText = snapshot.ColumnsText ?? ColumnsText;
        RowsText = snapshot.RowsText ?? RowsText;
        ThumbnailWidthText = snapshot.ThumbnailWidthText ?? ThumbnailWidthText;
        ThumbnailHeightText = snapshot.ThumbnailHeightText ?? ThumbnailHeightText;
        SpacingText = snapshot.SpacingText ?? SpacingText;
        ExportFormatIndex = snapshot.ExportFormatIndex ?? ExportFormatIndex;
        BackgroundHex = snapshot.BackgroundHex ?? BackgroundHex;
        MetadataHex = snapshot.MetadataHex ?? MetadataHex;
        FileNameFontSize = snapshot.FileNameFontSize ?? FileNameFontSize;
        DurationFontSize = snapshot.DurationFontSize ?? DurationFontSize;
        FileSizeFontSize = snapshot.FileSizeFontSize ?? FileSizeFontSize;
        ResolutionFontSize = snapshot.ResolutionFontSize ?? ResolutionFontSize;
        TimestampFontSize = snapshot.TimestampFontSize ?? TimestampFontSize;
        ShowFileName = snapshot.ShowFileName ?? ShowFileName;
        ShowDuration = snapshot.ShowDuration ?? ShowDuration;
        ShowFileSize = snapshot.ShowFileSize ?? ShowFileSize;
        ShowResolution = snapshot.ShowResolution ?? ShowResolution;
        ShowTimestamp = snapshot.ShowTimestamp ?? ShowTimestamp;
        ExportSeparateThumbnails = snapshot.ExportSeparateThumbnails ?? ExportSeparateThumbnails;
        RenderConcurrency = snapshot.RenderConcurrency ?? RenderConcurrency;
    }

    private sealed class AppSettingsSnapshot
    {
        public string? ColumnsText { get; init; }
        public string? RowsText { get; init; }
        public string? ThumbnailWidthText { get; init; }
        public string? ThumbnailHeightText { get; init; }
        public string? SpacingText { get; init; }
        public int? ExportFormatIndex { get; init; }
        public string? BackgroundHex { get; init; }
        public string? MetadataHex { get; init; }
        public float? FileNameFontSize { get; init; }
        public float? DurationFontSize { get; init; }
        public float? FileSizeFontSize { get; init; }
        public float? ResolutionFontSize { get; init; }
        public float? TimestampFontSize { get; init; }
        public bool? ShowFileName { get; init; }
        public bool? ShowDuration { get; init; }
        public bool? ShowFileSize { get; init; }
        public bool? ShowResolution { get; init; }
        public bool? ShowTimestamp { get; init; }
        public bool? ExportSeparateThumbnails { get; init; }
        public int? RenderConcurrency { get; init; }
    }
}
