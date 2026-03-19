using System.Diagnostics;
using System.Drawing;
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
                "-show_entries", "stream=codec_type,codec_name,width,height,duration,bit_rate:format=duration,bit_rate",
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
                .Select(stream => NormalizeCodec(stream.CodecName))
                .Where(codec => !string.IsNullOrWhiteSpace(codec))
                .ToList());
    }

    public async Task<IReadOnlyList<ThumbnailFrame>> GenerateThumbnailsAsync(
        string filePath,
        int count,
        int width,
        int height,
        TimeSpan duration,
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
            var filter = string.Create(
                CultureInfo.InvariantCulture,
                $"scale=w={maxWidth}:h={maxHeight}:force_original_aspect_ratio=decrease,pad={maxWidth}:{maxHeight}:(ow-iw)/2:(oh-ih)/2:color=black");

            var maxParallel = Math.Clamp(Environment.ProcessorCount / 3, 1, 3);
            using var gate = new SemaphoreSlim(maxParallel, maxParallel);
            var outputs = new string?[times.Count];

            var extractionTasks = times.Select((timestamp, index) => ExtractSingleFrameAsync(
                filePath,
                tempDir,
                index,
                timestamp,
                filter,
                gate,
                outputs,
                cancellationToken)).ToList();

            await Task.WhenAll(extractionTasks);

            for (var i = 0; i < outputs.Length && i < times.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(outputs[i]) || !File.Exists(outputs[i]))
                {
                    continue;
                }

                frames.Add(new ThumbnailFrame(outputs[i]!, times[i]));
            }

            if (frames.Count == 0)
            {
                throw new InvalidOperationException("Keine Thumbnails erzeugt.");
            }

            return frames;
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

        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        await process.WaitForExitAsync(linkedCts.Token);
        var stdout = await stdOutTask;
        var stderr = await stdErrTask;

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
        string filter,
        SemaphoreSlim gate,
        string?[] outputs,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
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
                    [
                        "-y",
                        "-hide_banner",
                        "-loglevel", "error",
                        "-nostdin",
                        "-ss", fastSeekSeconds.ToString("0.###", CultureInfo.InvariantCulture),
                        "-hwaccel", "auto",
                        "-i", filePath,
                        "-ss", preciseOffset.ToString("0.###", CultureInfo.InvariantCulture),
                        "-an",
                        "-sn",
                        "-dn",
                        "-frames:v", "1",
                        "-vf", filter,
                        "-vsync", "vfr",
                        "-c:v", "bmp",
                        output
                    ],
                    TimeSpan.FromSeconds(45),
                    cancellationToken);
            }
            catch
            {
                await RunProcessAsync(
                    _tools.FfmpegPath,
                    [
                        "-y",
                        "-hide_banner",
                        "-loglevel", "error",
                        "-nostdin",
                        "-ss", fastSeekSeconds.ToString("0.###", CultureInfo.InvariantCulture),
                        "-i", filePath,
                        "-ss", preciseOffset.ToString("0.###", CultureInfo.InvariantCulture),
                        "-an",
                        "-sn",
                        "-dn",
                        "-frames:v", "1",
                        "-vf", filter,
                        "-vsync", "vfr",
                        "-c:v", "bmp",
                        output
                    ],
                    TimeSpan.FromSeconds(45),
                    cancellationToken);
            }

            outputs[index] = output;
        }
        finally
        {
            gate.Release();
        }
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
    }

    private sealed class FfprobeFormat
    {
        [JsonPropertyName("duration")]
        public string? Duration { get; init; }

        [JsonPropertyName("bit_rate")]
        public string? BitRate { get; init; }
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
}
