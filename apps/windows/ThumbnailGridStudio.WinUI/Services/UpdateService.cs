using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ThumbnailGridStudio.WinUI.Services;

public sealed record ReleaseInfo(string TagName, string HtmlUrl, string Name, string Body);

public sealed record UpdateCheckResult(bool IsUpdateAvailable, string LocalVersion, ReleaseInfo? Release);

public static class UpdateService
{
    private static readonly Uri LatestReleaseUri = new("https://api.github.com/repos/leiweibau/ThumbnailGridStudio/releases/latest");

    public static async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ThumbnailGridStudioWinUI", "1.0"));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var response = await http.GetAsync(LatestReleaseUri, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

        var payload = await JsonSerializer.DeserializeAsync<GitHubReleasePayload>(stream, cancellationToken: cancellationToken)
                      ?? throw new InvalidOperationException("Ungültige Antwort von GitHub.");

        var localVersion = GetLocalVersionString();
        var remoteTag = payload.TagName ?? "0.0.0";
        var hasUpdate = IsRemoteVersionNewer(remoteTag, localVersion);

        var release = new ReleaseInfo(
            TagName: remoteTag,
            HtmlUrl: payload.HtmlUrl ?? "https://github.com/leiweibau/ThumbnailGridStudio/releases",
            Name: payload.Name ?? remoteTag,
            Body: payload.Body ?? string.Empty);

        return new UpdateCheckResult(hasUpdate, localVersion, release);
    }

    private static string GetLocalVersionString()
    {
        var assembly = typeof(UpdateService).Assembly;

        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var cleaned = informational.Split('+')[0].Trim();
            if (!string.IsNullOrWhiteSpace(cleaned))
            {
                return cleaned;
            }
        }

        var fileVersion = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
        if (!string.IsNullOrWhiteSpace(fileVersion))
        {
            return fileVersion;
        }

        return assembly.GetName().Version?.ToString() ?? "0.0.0";
    }

    private static bool IsRemoteVersionNewer(string remoteTag, string localVersion)
    {
        var remote = NormalizeVersion(remoteTag);
        var local = NormalizeVersion(localVersion);
        return remote > local;
    }

    private static Version NormalizeVersion(string input)
    {
        var cleaned = input.Trim();
        if (cleaned.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned[1..];
        }

        var plusIndex = cleaned.IndexOf('+');
        if (plusIndex >= 0)
        {
            cleaned = cleaned[..plusIndex];
        }

        var dashIndex = cleaned.IndexOf('-');
        if (dashIndex >= 0)
        {
            cleaned = cleaned[..dashIndex];
        }

        return Version.TryParse(cleaned, out var version) ? version : new Version(0, 0, 0);
    }

    private sealed class GitHubReleasePayload
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; init; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("body")]
        public string? Body { get; init; }
    }
}
