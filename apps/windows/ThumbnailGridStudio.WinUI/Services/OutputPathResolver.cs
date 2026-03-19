namespace ThumbnailGridStudio.WinUI.Services;

public static class OutputPathResolver
{
    public static string GetUniqueFilePath(string desiredPath)
    {
        if (string.IsNullOrWhiteSpace(desiredPath))
        {
            throw new ArgumentException("Output path must not be empty.", nameof(desiredPath));
        }

        var fullPath = Path.GetFullPath(desiredPath);
        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            return fullPath;
        }

        var directory = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
        var stem = Path.GetFileNameWithoutExtension(fullPath);
        var extension = Path.GetExtension(fullPath);
        var counter = 1;

        while (true)
        {
            var candidate = Path.Combine(directory, $"{stem} ({counter}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }

            counter++;
        }
    }

    public static string GetUniqueDirectoryPath(string desiredDirectoryPath)
    {
        if (string.IsNullOrWhiteSpace(desiredDirectoryPath))
        {
            throw new ArgumentException("Output directory path must not be empty.", nameof(desiredDirectoryPath));
        }

        var fullPath = Path.GetFullPath(desiredDirectoryPath);
        if (!Directory.Exists(fullPath) && !File.Exists(fullPath))
        {
            return fullPath;
        }

        var parent = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
        var name = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var counter = 1;

        while (true)
        {
            var candidate = Path.Combine(parent, $"{name} ({counter})");
            if (!Directory.Exists(candidate) && !File.Exists(candidate))
            {
                return candidate;
            }

            counter++;
        }
    }
}
