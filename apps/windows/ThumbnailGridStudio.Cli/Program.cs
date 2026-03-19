using System.Drawing.Imaging;
using System.Globalization;
using System.Reflection;
using ThumbnailGridStudio.WinUI.Models;
using ThumbnailGridStudio.WinUI.Services;

namespace ThumbnailGridStudio.Cli;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        CliOptions options;
        try
        {
            options = CliOptions.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine();
            PrintHelp();
            return 2;
        }

        if (options.ShowHelp)
        {
            PrintHelp();
            return 0;
        }

        if (options.InputPaths.Count == 0)
        {
            Console.Error.WriteLine("No input files or directories provided.");
            PrintHelp();
            return 2;
        }

        var inputs = ExpandInputPaths(options.InputPaths, options.RecursiveDirectoryScan);
        if (inputs.Count == 0)
        {
            Console.Error.WriteLine("No existing files found for the provided inputs.");
            return 2;
        }

        var settings = new AppSettings();
        await settings.LoadAsync();
        ApplyOverrides(settings, options.Overrides);

        var outputDirectory = ResolveOutputDirectory(options.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var tools = FfmpegService.ResolveTools();
        var processor = new VideoProcessingService(tools);

        var successCount = 0;
        var failureCount = 0;
        var completedCount = 0;
        var totalCount = inputs.Count;
        var sync = new object();
        using var gate = new SemaphoreSlim(Math.Max(1, settings.RenderConcurrency), Math.Max(1, settings.RenderConcurrency));

        var tasks = inputs.Select(async inputPath =>
        {
            await gate.WaitAsync();
            try
            {
                var result = await RenderSingleAsync(inputPath, outputDirectory, settings, processor);
                lock (sync)
                {
                    if (result.Succeeded)
                    {
                        successCount++;
                        Console.WriteLine($"[OK] {Path.GetFileName(inputPath)} -> {result.OutputPath}");
                    }
                    else
                    {
                        failureCount++;
                        Console.Error.WriteLine($"[ERR] {Path.GetFileName(inputPath)} -> {result.Error}");
                    }
                }
            }
            finally
            {
                var done = Interlocked.Increment(ref completedCount);
                Console.WriteLine($"Progress: {done}/{totalCount}");
                gate.Release();
            }
        }).ToList();

        await Task.WhenAll(tasks);

        Console.WriteLine();
        Console.WriteLine($"Done. Success: {successCount}, Failed: {failureCount}");
        Console.WriteLine($"Output folder: {outputDirectory}");
        return failureCount == 0 ? 0 : 1;
    }

    private static async Task<RenderResult> RenderSingleAsync(
        string inputPath,
        string outputDirectory,
        AppSettings settings,
        VideoProcessingService processor)
    {
        try
        {
            var metadata = await processor.LoadMetadataAsync(inputPath);
            var thumbSize = settings.ResolveThumbnailSize(metadata.Width, metadata.Height);
            var count = settings.Columns * settings.Rows;

            var thumbnails = (await processor.GenerateThumbnailsAsync(
                inputPath,
                count,
                thumbSize.Width,
                thumbSize.Height,
                metadata.Duration)).ToList();

            try
            {
                var outputPath = OutputPathResolver.GetUniqueFilePath(Path.Combine(
                    outputDirectory,
                    $"{Path.GetFileNameWithoutExtension(inputPath)}.{settings.ExportFileExtension}"));

                ContactSheetRenderer.RenderAndSave(metadata, Path.GetFileName(inputPath), thumbnails, settings, outputPath);

                if (settings.ExportSeparateThumbnails)
                {
                    var fullResolutionWidth = metadata.Width > 0 ? metadata.Width : settings.ThumbnailWidth;
                    var fullResolutionHeight = metadata.Height > 0 ? metadata.Height : settings.ThumbnailHeight;
                    var fullResolution = (await processor.GenerateThumbnailsAsync(
                        inputPath,
                        count,
                        fullResolutionWidth,
                        fullResolutionHeight,
                        metadata.Duration)).ToList();
                    try
                    {
                        ExportSeparateThumbnails(inputPath, fullResolution, outputDirectory, settings.ExportFormatIndex);
                    }
                    finally
                    {
                        foreach (var frame in fullResolution)
                        {
                            frame.Dispose();
                        }
                    }
                }

                return RenderResult.Success(outputPath);
            }
            finally
            {
                foreach (var frame in thumbnails)
                {
                    frame.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            return RenderResult.Fail(ex.Message);
        }
    }

    private static void ExportSeparateThumbnails(
        string inputPath,
        IReadOnlyList<ThumbnailFrame> thumbnails,
        string outputDirectory,
        int exportFormatIndex)
    {
        if (thumbnails.Count == 0)
        {
            return;
        }

        var stem = Path.GetFileNameWithoutExtension(inputPath);
        var folder = OutputPathResolver.GetUniqueDirectoryPath(Path.Combine(outputDirectory, stem));
        Directory.CreateDirectory(folder);

        var format = exportFormatIndex == 1
            ? ImageFormat.Png
            : ImageFormat.Jpeg;
        var extension = exportFormatIndex == 1 ? "png" : "jpg";

        for (var i = 0; i < thumbnails.Count; i++)
        {
            var frame = thumbnails[i];
            var timestamp = FormatTimestampForFileName(frame.Timestamp);
            var path = Path.Combine(folder, $"{i + 1:000}_{timestamp}.{extension}");
            frame.Image.Save(path, format);
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

    private static string ResolveOutputDirectory(string? overrideOutputDirectory)
    {
        if (!string.IsNullOrWhiteSpace(overrideOutputDirectory))
        {
            return Path.GetFullPath(overrideOutputDirectory);
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "ThumbnailGridStudio",
            "Exports");
    }

    private static List<string> ExpandInputPaths(IEnumerable<string> rawInputs, bool recursive)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawInput in rawInputs)
        {
            if (string.IsNullOrWhiteSpace(rawInput))
            {
                continue;
            }

            var path = Path.GetFullPath(rawInput.Trim());
            if (File.Exists(path))
            {
                files.Add(path);
                continue;
            }

            if (Directory.Exists(path))
            {
                try
                {
                    var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                    foreach (var file in Directory.EnumerateFiles(path, "*", option))
                    {
                        files.Add(file);
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[WARN] Unable to enumerate directory '{path}': {ex.Message}");
                }

                continue;
            }

            Console.Error.WriteLine($"[WARN] Input path does not exist: {path}");
        }

        return files.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void ApplyOverrides(AppSettings settings, IReadOnlyDictionary<string, string> overrides)
    {
        foreach (var (key, value) in overrides)
        {
            switch (key)
            {
                case "columns":
                    settings.ColumnsText = value;
                    break;
                case "rows":
                    settings.RowsText = value;
                    break;
                case "width":
                case "thumb-width":
                    settings.ThumbnailWidthText = NormalizeDimensionValue(value);
                    break;
                case "height":
                case "thumb-height":
                    settings.ThumbnailHeightText = NormalizeDimensionValue(value);
                    break;
                case "spacing":
                    settings.SpacingText = value;
                    break;
                case "format":
                    settings.ExportFormatIndex = ParseFormat(value);
                    break;
                case "background":
                    settings.BackgroundHex = value;
                    break;
                case "metadata-color":
                case "text-color":
                    settings.MetadataHex = value;
                    break;
                case "show-title":
                    settings.ShowFileName = ParseBool(value, key);
                    break;
                case "show-duration":
                    settings.ShowDuration = ParseBool(value, key);
                    break;
                case "show-file-size":
                    settings.ShowFileSize = ParseBool(value, key);
                    break;
                case "show-resolution":
                    settings.ShowResolution = ParseBool(value, key);
                    break;
                case "show-timestamp":
                    settings.ShowTimestamp = ParseBool(value, key);
                    break;
                case "show-bitrate":
                    settings.ShowBitrate = ParseBool(value, key);
                    break;
                case "show-video-codec":
                    settings.ShowVideoCodec = ParseBool(value, key);
                    break;
                case "show-audio-codec":
                    settings.ShowAudioCodec = ParseBool(value, key);
                    break;
                case "title-font":
                    settings.FileNameFontSize = ParseFloat(value, key);
                    break;
                case "duration-font":
                    settings.DurationFontSize = ParseFloat(value, key);
                    break;
                case "file-size-font":
                    settings.FileSizeFontSize = ParseFloat(value, key);
                    break;
                case "resolution-font":
                    settings.ResolutionFontSize = ParseFloat(value, key);
                    break;
                case "timestamp-font":
                    settings.TimestampFontSize = ParseFloat(value, key);
                    break;
                case "bitrate-font":
                    settings.BitrateFontSize = ParseFloat(value, key);
                    break;
                case "video-codec-font":
                    settings.VideoCodecFontSize = ParseFloat(value, key);
                    break;
                case "audio-codec-font":
                    settings.AudioCodecFontSize = ParseFloat(value, key);
                    break;
                case "export-separate":
                    settings.ExportSeparateThumbnails = ParseBool(value, key);
                    break;
                case "concurrency":
                    settings.RenderConcurrency = ParseInt(value, key, 1, 8);
                    break;
                default:
                    throw new ArgumentException($"Unknown option: --{key}");
            }
        }
    }

    private static int ParseFormat(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "jpg" => 0,
            "jpeg" => 0,
            "png" => 1,
            _ => throw new ArgumentException($"Invalid value for --format: {value}. Use jpg or png.")
        };
    }

    private static string NormalizeDimensionValue(string value)
    {
        return value.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase) ? string.Empty : value;
    }

    private static int ParseInt(string value, string optionName, int min, int max)
    {
        if (!int.TryParse(value, out var parsed))
        {
            throw new ArgumentException($"Invalid integer for --{optionName}: {value}");
        }

        return Math.Clamp(parsed, min, max);
    }

    private static float ParseFloat(string value, string optionName)
    {
        var normalized = value.Replace(',', '.');
        if (!float.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new ArgumentException($"Invalid float for --{optionName}: {value}");
        }

        return Math.Clamp(parsed, 8f, 96f);
    }

    private static bool ParseBool(string value, string optionName)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "1" => true,
            "0" => false,
            "true" => true,
            "false" => false,
            "yes" => true,
            "no" => false,
            "on" => true,
            "off" => false,
            _ => throw new ArgumentException($"Invalid boolean for --{optionName}: {value}")
        };
    }

    private static void PrintHelp()
    {
        Console.WriteLine($"ThumbnailGridStudio-cli {GetDisplayVersion()}");
        Console.WriteLine();
        Console.WriteLine("""
Usage:
  ThumbnailGridStudio-cli --input <file-or-dir> [--input <file-or-dir> ...] [options]

Options:
  -i, --input <path>            Input video file or directory (repeatable)
  -o, --output-dir <dir>        Output directory (default: %USERPROFILE%\Pictures\ThumbnailGridStudio\Exports)
      --recursive <bool>        Scan directories recursively (default: true)
      --columns <int>
      --rows <int>
      --width <int|auto>
      --height <int|auto>
      --spacing <int>
      --format <jpg|png>
      --background <HEX>
      --metadata-color <HEX>
      --show-title <bool>
      --show-duration <bool>
      --show-file-size <bool>
      --show-resolution <bool>
      --show-timestamp <bool>
      --show-bitrate <bool>
      --show-video-codec <bool>
      --show-audio-codec <bool>
      --title-font <px>
      --duration-font <px>
      --file-size-font <px>
      --resolution-font <px>
      --timestamp-font <px>
      --bitrate-font <px>
      --video-codec-font <px>
      --audio-codec-font <px>
      --export-separate <bool>
      --concurrency <1-8>
  -h, --help                    Show this help

Notes:
  Settings are loaded from the GUI app settings file first
  (%LOCALAPPDATA%\ThumbnailGridStudio\settings.json).
  Any CLI option above overrides the loaded settings.
  Legacy aliases are still accepted: --output, --thumb-width, --thumb-height, --text-color.
""");
    }

    private static string GetDisplayVersion()
    {
        var assembly = typeof(Program).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plusIndex = informational.IndexOf('+');
            return plusIndex >= 0 ? informational[..plusIndex] : informational;
        }

        return assembly.GetName().Version?.ToString() ?? "unknown";
    }

    private readonly record struct RenderResult(bool Succeeded, string? OutputPath, string? Error)
    {
        public static RenderResult Success(string outputPath) => new(true, outputPath, null);
        public static RenderResult Fail(string error) => new(false, null, error);
    }

    private sealed class CliOptions
    {
        public bool ShowHelp { get; private set; }
        public List<string> InputPaths { get; } = [];
        public string? OutputDirectory { get; private set; }
        public bool RecursiveDirectoryScan { get; private set; } = true;
        public Dictionary<string, string> Overrides { get; } = new(StringComparer.OrdinalIgnoreCase);

        public static CliOptions Parse(string[] args)
        {
            var options = new CliOptions();
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (!arg.StartsWith('-'))
                {
                    options.InputPaths.Add(arg);
                    continue;
                }

                switch (arg)
                {
                    case "-h":
                    case "--help":
                        options.ShowHelp = true;
                        break;
                    case "-i":
                    case "--input":
                        options.InputPaths.Add(ReadValue(args, ref i, arg));
                        break;
                    case "-o":
                    case "--output-dir":
                    case "--output":
                        options.OutputDirectory = ReadValue(args, ref i, arg);
                        break;
                    case "--recursive":
                        options.RecursiveDirectoryScan = ParseBool(ReadValue(args, ref i, arg), "recursive");
                        break;
                    default:
                        if (!arg.StartsWith("--", StringComparison.Ordinal))
                        {
                            throw new ArgumentException($"Unknown option: {arg}");
                        }

                        var key = arg[2..];
                        var value = ReadValue(args, ref i, arg);
                        options.Overrides[key] = value;
                        break;
                }
            }

            return options;
        }

        private static string ReadValue(string[] args, ref int index, string optionName)
        {
            if (index + 1 >= args.Length)
            {
                throw new ArgumentException($"Missing value for {optionName}");
            }

            index++;
            return args[index];
        }
    }
}
