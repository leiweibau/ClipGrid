using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ThumbnailGridStudio.WinUI.Services;

public sealed record VideoMetadata(
    TimeSpan Duration,
    int Width,
    int Height,
    long FileSizeBytes,
    long BitrateBitsPerSecond,
    string VideoCodec,
    IReadOnlyList<string> AudioCodecs);

public sealed class ThumbnailFrame : IDisposable
{
    public ThumbnailFrame(string imagePath, TimeSpan timestamp)
    {
        using var loaded = new Bitmap(imagePath);
        Image = new Bitmap(loaded);
        Timestamp = timestamp;
    }

    public ThumbnailFrame(Bitmap image, TimeSpan timestamp)
    {
        Image = new Bitmap(image);
        Timestamp = timestamp;
    }

    public Bitmap Image { get; }
    public TimeSpan Timestamp { get; }

    public void Dispose()
    {
        Image.Dispose();
    }
}

public sealed class VideoProcessingService
{
    private static readonly int MaxConcurrentFrameExtractions = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
    private static readonly SemaphoreSlim FrameExtractionGate = new(
        MaxConcurrentFrameExtractions,
        MaxConcurrentFrameExtractions);
    private readonly FfmpegTools _tools;

    public VideoProcessingService(FfmpegTools tools)
    {
        _tools = tools;
    }

    public async Task<VideoMetadata> LoadMetadataAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(filePath);
        var (stdout, stderr) = await RunProcessAsync(
            _tools.FfprobePath,
            [
                "-v", "error",
                "-show_entries", "stream=codec_type,codec_name,width,height,duration,bit_rate:stream_tags=language:format=duration,bit_rate",
                "-of", "json",
                filePath
            ],
            TimeSpan.FromSeconds(25),
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(stderr))
        {
            throw new InvalidOperationException(stderr.Trim());
        }

        var payload = JsonSerializer.Deserialize<FfprobePayload>(
            stdout,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                      ?? throw new InvalidOperationException("ffprobe JSON konnte nicht gelesen werden.");

        var videoStream = payload.Streams.FirstOrDefault(stream =>
            string.Equals(stream.CodecType, "video", StringComparison.OrdinalIgnoreCase));
        var audioStreams = payload.Streams
            .Where(stream => string.Equals(stream.CodecType, "audio", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (videoStream is null)
        {
            throw new InvalidOperationException("No video stream found.");
        }

        var width = (int)Math.Round(videoStream.Width ?? 0);
        var height = (int)Math.Round(videoStream.Height ?? 0);
        var seconds = ParseDurationSeconds(payload.Format?.Duration);
        if (seconds <= 0)
        {
            seconds = ParseDurationSeconds(videoStream.Duration);
        }
        if (seconds <= 0)
        {
            throw new InvalidOperationException("Unsupported media type: missing valid video duration.");
        }

        var bitrate = ParseBitrateBitsPerSecond(payload.Format?.BitRate);
        if (bitrate <= 0)
        {
            bitrate = ParseBitrateBitsPerSecond(videoStream.BitRate);
        }

        return new VideoMetadata(
            Duration: TimeSpan.FromSeconds(Math.Max(seconds, 0)),
            Width: Math.Max(width, 0),
            Height: Math.Max(height, 0),
            FileSizeBytes: info.Exists ? info.Length : 0,
            BitrateBitsPerSecond: Math.Max(bitrate, 0),
            VideoCodec: NormalizeCodec(videoStream.CodecName),
            AudioCodecs: audioStreams
                .Select(stream => BuildAudioCodecWithLanguage(stream.CodecName, stream.Tags?.Language))
                .Where(codec => !string.IsNullOrWhiteSpace(codec))
                .ToList());
    }

    public async Task<IReadOnlyList<ThumbnailFrame>> GenerateThumbnailsAsync(
        string filePath,
        int count,
        int width,
        int height,
        TimeSpan duration,
        Func<int, ThumbnailFrame, CancellationToken, Task>? fullResolutionFrameHandler = null,
        CancellationToken cancellationToken = default)
    {
        var frameCount = Math.Max(1, count);
        var times = BuildFrameTimes(duration, frameCount);
        var frames = new List<ThumbnailFrame>(frameCount);

        var tempDir = Path.Combine(Path.GetTempPath(), "thumbnail-grid-studio", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var maxWidth = Math.Max(width, 1);
            var maxHeight = Math.Max(height, 1);
            var filter = fullResolutionFrameHandler is null
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"scale=w={maxWidth}:h={maxHeight}:force_original_aspect_ratio=decrease,pad={maxWidth}:{maxHeight}:(ow-iw)/2:(oh-ih)/2:color=black")
                : null;
            var outputs = new string?[times.Count];

            var extractionTasks = times.Select((timestamp, index) => ExtractSingleFrameAsync(
                filePath,
                tempDir,
                index,
                timestamp,
                filter,
                outputs,
                cancellationToken)).ToList();

            await Task.WhenAll(extractionTasks).ConfigureAwait(false);

            for (var i = 0; i < outputs.Length && i < times.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(outputs[i]) || !File.Exists(outputs[i]))
                {
                    continue;
                }

                if (fullResolutionFrameHandler is null)
                {
                    frames.Add(new ThumbnailFrame(outputs[i]!, times[i]));
                    continue;
                }

                using var fullResolutionFrame = new ThumbnailFrame(outputs[i]!, times[i]);
                await fullResolutionFrameHandler(i, fullResolutionFrame, cancellationToken).ConfigureAwait(false);
                using var resized = ResizeAndPad(fullResolutionFrame.Image, maxWidth, maxHeight);
                frames.Add(new ThumbnailFrame(resized, times[i]));
            }

            if (frames.Count == 0)
            {
                throw new InvalidOperationException("Keine Thumbnails erzeugt.");
            }

            return frames;
        }
        catch
        {
            foreach (var frame in frames)
            {
                frame.Dispose();
            }

            throw;
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, true);
            }
            catch
            {
                // Ignore temp cleanup failures.
            }
        }
    }

    private static List<TimeSpan> BuildFrameTimes(TimeSpan duration, int count)
    {
        var result = new List<TimeSpan>(count);
        if (count <= 1)
        {
            result.Add(TimeSpan.FromSeconds(Math.Max(duration.TotalSeconds, 0.1) / 2d));
            return result;
        }

        var totalSeconds = Math.Max(duration.TotalSeconds, 0.1);
        var start = totalSeconds * 0.05;
        var end = totalSeconds * 0.95;
        var step = (end - start) / (count - 1);

        for (var i = 0; i < count; i++)
        {
            result.Add(TimeSpan.FromSeconds(start + i * step));
        }

        return result;
    }

    private static async Task<(string Stdout, string Stderr)> RunProcessAsync(
        string executable,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdOutTask = process.StandardOutput.ReadToEndAsync();
        var stdErrTask = process.StandardError.ReadToEndAsync();
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // The process may have exited between the checks.
            }

            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            throw new TimeoutException($"{Path.GetFileName(executable)} exceeded the {timeout.TotalSeconds:0}-second timeout.");
        }

        var stdout = await stdOutTask.ConfigureAwait(false);
        var stderr = await stdErrTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            var errorText = string.IsNullOrWhiteSpace(stderr) ? $"Exit code {process.ExitCode}" : stderr.Trim();
            throw new InvalidOperationException(errorText);
        }

        return (stdout, stderr);
    }

    private async Task ExtractSingleFrameAsync(
        string filePath,
        string tempDir,
        int index,
        TimeSpan timestamp,
        string? filter,
        string?[] outputs,
        CancellationToken cancellationToken)
    {
        await FrameExtractionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var output = Path.Combine(tempDir, $"thumb-{index + 1:000}.bmp");
            var seconds = Math.Max(timestamp.TotalSeconds, 0);
            var fastSeekSeconds = Math.Max(0, seconds - 2.0d);
            var preciseOffset = Math.Max(0, seconds - fastSeekSeconds);

            try
            {
                await RunProcessAsync(
                    _tools.FfmpegPath,
                    BuildFrameArguments(filePath, output, fastSeekSeconds, preciseOffset, filter, useHardwareAcceleration: true),
                    TimeSpan.FromSeconds(45),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                await RunProcessAsync(
                    _tools.FfmpegPath,
                    BuildFrameArguments(filePath, output, fastSeekSeconds, preciseOffset, filter, useHardwareAcceleration: false),
                    TimeSpan.FromSeconds(45),
                    cancellationToken).ConfigureAwait(false);
            }

            outputs[index] = output;
        }
        finally
        {
            FrameExtractionGate.Release();
        }
    }

    private static IReadOnlyList<string> BuildFrameArguments(
        string filePath,
        string outputPath,
        double fastSeekSeconds,
        double preciseOffset,
        string? filter,
        bool useHardwareAcceleration)
    {
        var arguments = new List<string>
        {
            "-y",
            "-hide_banner",
            "-loglevel", "error",
            "-nostdin",
            "-ss", fastSeekSeconds.ToString("0.###", CultureInfo.InvariantCulture)
        };

        if (useHardwareAcceleration)
        {
            arguments.Add("-hwaccel");
            arguments.Add("auto");
        }

        arguments.AddRange([
            "-i", filePath,
            "-ss", preciseOffset.ToString("0.###", CultureInfo.InvariantCulture),
            "-an",
            "-sn",
            "-dn",
            "-frames:v", "1"
        ]);

        if (!string.IsNullOrWhiteSpace(filter))
        {
            arguments.Add("-vf");
            arguments.Add(filter);
        }

        arguments.AddRange([
            "-vsync", "vfr",
            "-c:v", "bmp",
            outputPath
        ]);
        return arguments;
    }

    private static Bitmap ResizeAndPad(Bitmap source, int width, int height)
    {
        var targetWidth = Math.Max(width, 1);
        var targetHeight = Math.Max(height, 1);
        var result = new Bitmap(targetWidth, targetHeight);
        using var graphics = Graphics.FromImage(result);
        graphics.Clear(Color.Black);
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var scale = Math.Min(targetWidth / (double)Math.Max(source.Width, 1), targetHeight / (double)Math.Max(source.Height, 1));
        var drawWidth = Math.Max((int)Math.Round(source.Width * scale), 1);
        var drawHeight = Math.Max((int)Math.Round(source.Height * scale), 1);
        var drawX = (targetWidth - drawWidth) / 2;
        var drawY = (targetHeight - drawHeight) / 2;
        graphics.DrawImage(source, drawX, drawY, drawWidth, drawHeight);
        return result;
    }

    private sealed class FfprobePayload
    {
        public List<FfprobeStream> Streams { get; init; } = [];
        public FfprobeFormat? Format { get; init; }
    }

    private sealed class FfprobeStream
    {
        [JsonPropertyName("codec_type")]
        public string? CodecType { get; init; }

        [JsonPropertyName("codec_name")]
        public string? CodecName { get; init; }

        [JsonPropertyName("width")]
        public double? Width { get; init; }

        [JsonPropertyName("height")]
        public double? Height { get; init; }

        [JsonPropertyName("duration")]
        public string? Duration { get; init; }

        [JsonPropertyName("bit_rate")]
        public string? BitRate { get; init; }

        [JsonPropertyName("tags")]
        public FfprobeStreamTags? Tags { get; init; }
    }

    private sealed class FfprobeFormat
    {
        [JsonPropertyName("duration")]
        public string? Duration { get; init; }

        [JsonPropertyName("bit_rate")]
        public string? BitRate { get; init; }
    }

    private sealed class FfprobeStreamTags
    {
        [JsonPropertyName("language")]
        public string? Language { get; init; }
    }

    private static double ParseDurationSeconds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            return 0;
        }

        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0)
        {
            return 0;
        }

        return seconds;
    }

    private static long ParseBitrateBitsPerSecond(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bitrate))
        {
            return 0;
        }

        return bitrate > 0 ? bitrate : 0;
    }

    private static string NormalizeCodec(string? codecName)
    {
        return string.IsNullOrWhiteSpace(codecName) ? string.Empty : codecName.Trim();
    }

    private static string BuildAudioCodecWithLanguage(string? codecName, string? languageTag)
    {
        var codec = NormalizeCodec(codecName);
        if (string.IsNullOrWhiteSpace(codec))
        {
            return string.Empty;
        }

        var language = NormalizeLanguageTag(languageTag);
        return string.IsNullOrWhiteSpace(language)
            ? codec
            : $"{codec} ({language})";
    }

    private static string NormalizeLanguageTag(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().ToLowerInvariant().Replace('_', '-');
        return normalized is "und" or "unknown" or "unk"
            ? string.Empty
            : normalized;
    }
}
