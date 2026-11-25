using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;

namespace SimTrade.Services;

public static class CompanyLogoCache
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly ConcurrentDictionary<string, Task<byte[]?>> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SimTrade",
        "logos");

    public static Task<byte[]?> GetAsync(string symbol)
    {
        var normalized = Normalize(symbol);
        if (normalized is null)
            return Task.FromResult<byte[]?>(null);

        return Cache.GetOrAdd(normalized, LoadAsync);
    }

    private static async Task<byte[]?> LoadAsync(string symbol)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var path = Path.Combine(Folder, SafeFileName(symbol) + ".img");
            if (File.Exists(path))
            {
                var stored = await File.ReadAllBytesAsync(path);
                if (IsImage(stored))
                    return stored;
            }

            var bytes = await Http.GetByteArrayAsync($"https://financialmodelingprep.com/image-stock/{symbol}.png");
            if (!IsImage(bytes))
                return null;

            await File.WriteAllBytesAsync(path, bytes);
            return bytes;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return null;
        }
    }

    private static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var text = raw.Trim().ToUpperInvariant().Replace(" ", "");
        if (text.Length is 0 or > 24)
            return null;

        foreach (var character in text)
        {
            if (!char.IsLetterOrDigit(character) && character is not '.' and not '-' and not '^')
                return null;
        }

        return text;
    }

    private static string SafeFileName(string symbol)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(symbol.Select(character => invalid.Contains(character) ? '_' : character));
    }

    private static bool IsImage(byte[] bytes)
    {
        if (bytes.Length < 32)
            return false;

        if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            return true;

        if (bytes[0] == 0xFF && bytes[1] == 0xD8)
            return true;

        return bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.Accept.ParseAdd("image/png,image/*;q=0.8");
        return client;
    }
}
