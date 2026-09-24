using Serilog;
using System.Reflection;
using System.Text.Json;

public static class UpdateChecker
{
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/dms137/gamingMonitor/releases/latest";

    /// <summary>
    /// Checks GitHub for a newer release. Shows a toast if one is available.
    /// Never throws, never blocks startup.
    /// </summary>
    public static async Task CheckOnStartupAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("GamingMonitor");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            string json = await http.GetStringAsync(LatestReleaseApiUrl);
            using var doc = JsonDocument.Parse(json);
            string? tag = doc.RootElement.GetProperty("tag_name").GetString();
            string? pageUrl = doc.RootElement.GetProperty("html_url").GetString();

            if (string.IsNullOrWhiteSpace(tag))
            {
                return;
            }

            Version? current = Assembly.GetExecutingAssembly().GetName().Version;
            if (current == null || !Version.TryParse(tag.TrimStart('v', 'V'), out Version? latest))
            {
                return;
            }

            if (latest > current)
            {
                Log.Information($"[Update] New version available: {tag} (current {current}).");
                string? zipUrl = FindZipAsset(doc.RootElement);
                if (!string.IsNullOrWhiteSpace(zipUrl))
                {
                    NotificationHelper.ShowUpdateAvailableNotification(tag, tag, zipUrl);
                }
                else if (!string.IsNullOrWhiteSpace(pageUrl))
                {
                    NotificationHelper.ShowReleasePageNotification(tag, pageUrl);
                }
            }
            else
            {
                Log.Information($"[Update] Up to date: {current}.");
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"[Update] Version check failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Returns the download URL of the first .zip release asset, if any.
    /// </summary>
    private static string? FindZipAsset(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out JsonElement assets))
        {
            return null;
        }

        foreach (JsonElement asset in assets.EnumerateArray())
        {
            string? name = asset.TryGetProperty("name", out JsonElement nameProp) ? nameProp.GetString() : null;
            string? downloadUrl = asset.TryGetProperty("browser_download_url", out JsonElement urlProp) ? urlProp.GetString() : null;
            if (!string.IsNullOrWhiteSpace(name) &&
                name.StartsWith("GamingMonitor-", StringComparison.OrdinalIgnoreCase) &&
                name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(downloadUrl))
            {
                return downloadUrl;
            }
        }

        return null;
    }
}
