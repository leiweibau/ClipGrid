using ThumbnailGridStudio.WinUI.Helpers;

namespace ThumbnailGridStudio.WinUI.Models;

public sealed class VideoItem : ObservableObject
{
    private string _statusText = "Bereit";
    private string? _outputPath;

    public required string FilePath { get; init; }
    public required string FileName { get; init; }
    public required TimeSpan Duration { get; init; }
    public required long FileSizeBytes { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }

    public string MetadataText =>
        $"{FormatDuration(Duration)}  •  {FormatFileSize(FileSizeBytes)}  •  {Math.Max(Width, 0)} x {Math.Max(Height, 0)} px";

    public string DurationText => FormatDuration(Duration);
    public string FileSizeText => FormatFileSize(FileSizeBytes);
    public string ResolutionText => $"{Math.Max(Width, 0)} x {Math.Max(Height, 0)} px";

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public string? OutputPath
    {
        get => _outputPath;
        set => SetProperty(ref _outputPath, value);
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}";
        }

        return $"{duration.Minutes:00}:{duration.Seconds:00}";
    }

    private static string FormatFileSize(long size)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(size, 0);
        var suffix = 0;
        while (value >= 1024 && suffix < suffixes.Length - 1)
        {
            value /= 1024;
            suffix += 1;
        }

        return $"{value:0.##} {suffixes[suffix]}";
    }
}
