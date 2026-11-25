using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Windows;

namespace SimTrade.Services;

public sealed record AppUpdateInfo(
    Version Version,
    string TagName,
    string ReleaseName,
    string? ReleaseNotes,
    string SetupFileName,
    string DownloadUrl,
    long SizeBytes,
    string HtmlUrl);

public sealed class AppUpdateService
{
    public const string GitHubOwner = "spadron15";
    public const string GitHubRepo = "SimTrade";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SimTrade-Updater");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    public static Version CurrentVersion
    {
        get
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version is null ? new Version(1, 0, 0) : new Version(version.Major, version.Minor, version.Build);
        }
    }

    public async Task<AppUpdateInfo?> CheckLatestAsync(CancellationToken cancellationToken = default)
    {
        var url = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";
        using var response = await Http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = doc.RootElement;
        if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean())
            return null;
        if (root.TryGetProperty("prerelease", out var pre) && pre.GetBoolean())
            return null;

        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!TryParseVersion(tag, out var remoteVersion))
            return null;

        if (remoteVersion <= CurrentVersion)
            return null;

        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;

        JsonElement? setup = null;
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                continue;
            if (name.Contains("Setup", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("SimTrade-", StringComparison.OrdinalIgnoreCase))
            {
                setup = asset;
                break;
            }
        }

        if (setup is null)
            return null;

        var fileName = setup.Value.GetProperty("name").GetString() ?? "SimTrade-Setup.exe";
        var downloadUrl = setup.Value.GetProperty("browser_download_url").GetString();
        if (string.IsNullOrWhiteSpace(downloadUrl))
            return null;

        var size = setup.Value.TryGetProperty("size", out var sizeEl) ? sizeEl.GetInt64() : 0;
        return new AppUpdateInfo(
            remoteVersion,
            tag,
            root.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? tag : tag,
            root.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() : null,
            fileName,
            downloadUrl,
            size,
            root.TryGetProperty("html_url", out var htmlEl) ? htmlEl.GetString() ?? "" : "");
    }

    public async Task<string> DownloadSetupAsync(
        AppUpdateInfo update,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var folder = Path.Combine(Path.GetTempPath(), "SimTradeUpdates");
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, update.SetupFileName);
        if (File.Exists(target))
            File.Delete(target);

        using var response = await Http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? update.SizeBytes;

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
        var buffer = new byte[81920];
        long readTotal = 0;
        int read;
        while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            readTotal += read;
            if (total > 0)
                progress?.Report(Math.Min(100, readTotal * 100.0 / total));
        }

        progress?.Report(100);
        return target;
    }

    public static void LaunchInstallerAndExit(string setupPath)
    {
        if (!File.Exists(setupPath))
            throw new FileNotFoundException("No se encontró el instalador descargado.", setupPath);

        var start = new ProcessStartInfo
        {
            FileName = setupPath,
            // Cierra SimTrade, actualiza en silencio relativo y vuelve a abrir.
            Arguments = "/SILENT /CLOSEAPPLICATIONS /NORESTART /FORCECLOSEAPPLICATIONS",
            UseShellExecute = true
        };
        Process.Start(start);
        Application.Current.Dispatcher.InvokeAsync(() => Application.Current.Shutdown());
    }

    public static bool TryParseVersion(string? text, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var cleaned = text.Trim();
        if (cleaned.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned[1..];

        // SimTrade-Setup-1.0.70.exe → 1.0.70
        var setupPrefix = "SimTrade-Setup-";
        if (cleaned.StartsWith(setupPrefix, StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned[setupPrefix.Length..];
            if (cleaned.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                cleaned = cleaned[..^4];
        }

        if (!Version.TryParse(cleaned, out var parsed))
            return false;

        version = new Version(
            Math.Max(parsed.Major, 0),
            Math.Max(parsed.Minor, 0),
            parsed.Build < 0 ? 0 : parsed.Build);
        return true;
    }
}
