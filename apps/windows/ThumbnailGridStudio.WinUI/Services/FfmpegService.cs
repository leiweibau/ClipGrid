using System.Runtime.InteropServices;

namespace ThumbnailGridStudio.WinUI.Services;

public sealed record FfmpegTools(string FfmpegPath, string FfprobePath);

public static class FfmpegService
{
    public static FfmpegTools ResolveTools()
    {
        var ffmpeg = ResolveTool("THUMBNAIL_GRID_STUDIO_FFMPEG", "ffmpeg.exe");
        var ffprobe = ResolveTool("THUMBNAIL_GRID_STUDIO_FFPROBE", "ffprobe.exe");
        return new FfmpegTools(ffmpeg, ffprobe);
    }

    private static string ResolveTool(string envVar, string executableName)
    {
        var explicitPath = Environment.GetEnvironmentVariable(envVar);
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
        {
            return explicitPath;
        }

        var bundled = GetBundledPath(executableName);
        if (File.Exists(bundled))
        {
            return bundled;
        }

        var fromPath = ResolveFromPath(executableName);
        if (fromPath is not null)
        {
            return fromPath;
        }

        throw new FileNotFoundException(
            $"{executableName} wurde nicht gefunden. Lege {executableName} unter " +
            $"`Tools\\{CurrentArchFolder()}\\` ab oder setze {envVar}.");
    }

    private static string GetBundledPath(string executableName)
    {
        return Path.Combine(AppContext.BaseDirectory, "Tools", CurrentArchFolder(), executableName);
    }

    private static string CurrentArchFolder()
    {
        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "win-arm64",
            _ => "win-x64"
        };
    }

    private static string? ResolveFromPath(string executableName)
    {
        var rawPath = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return null;
        }

        foreach (var pathEntry in rawPath.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(pathEntry, executableName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
