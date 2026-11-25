using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;

namespace SimTrade.Services;

/// <summary>
/// Carga iconos de cuenta: emoji como PNG transparente (Twemoji) o URL de imagen gratuita.
/// Se muestran sobre fondo negro, igual que los logos de empresas.
/// </summary>
public static class AccountIconCache
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly ConcurrentDictionary<string, Task<byte[]?>> Cache = new(StringComparer.Ordinal);
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SimTrade",
        "account-icons");

    public static readonly IReadOnlyList<AccountIconCategory> Categories =
    [
        new("Finanzas",
        [
            "📈", "📉", "📊", "💰", "💵", "💴", "💶", "💷",
            "💳", "🏦", "💼", "🧾", "⚖️", "🪙", "💎", "🏆"
        ]),
        new("Crypto",
        [
            "₿", "Ξ", "🪙", "🔗", "🌐", "🛰️", "📡", "💾",
            "🖥️", "⚙️", "🔐", "🔑", "🧱", "♾️", "🌀", "🧿"
        ]),
        new("Estrategia",
        [
            "🎯", "🚀", "⚡", "🔥", "🛡️", "🗡️", "🧭", "🗺️",
            "🧠", "👁️", "🧩", "🎲", "♟️", "🏁", "🎳", "🎪"
        ]),
        new("Naturaleza",
        [
            "🌊", "🍀", "🌿", "🌲", "🌵", "🌴", "🌙", "☀️",
            "⭐", "🌟", "☁️", "❄️", "🌈", "🌋", "🪨", "💧"
        ]),
        new("Animales",
        [
            "🦊", "🐉", "🦅", "🐺", "🦁", "🐯", "🐻", "🐼",
            "🦉", "🐬", "🦈", "🐙", "🦄", "🐝", "🐢", "🦩"
        ]),
        new("Estilo",
        [
            "🧪", "🎨", "🎭", "🎬", "🎵", "🎮", "🪐", "👽",
            "👑", "💍", "🎁", "🕯️", "🪞", "🪄", "🧿", "♠️"
        ])
    ];

    public static IReadOnlyList<string> PresetEmojis =>
        Categories.SelectMany(category => category.Emojis).Distinct().ToArray();

    public readonly record struct AccountIconCategory(string Name, IReadOnlyList<string> Emojis);

    public static Task<byte[]?> GetAsync(string? icon)
    {
        var key = Normalize(icon);
        if (key is null)
            return Task.FromResult<byte[]?>(null);

        return Cache.GetOrAdd(key, LoadAsync);
    }

    public static string Normalize(string? icon)
    {
        if (string.IsNullOrWhiteSpace(icon))
            return "";

        var text = icon.Trim();
        if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (text.Length > 500)
                return "";
            return text;
        }

        // Un solo emoji / secuencia corta (incluye ZWJ).
        if (text.Length > 16)
            text = text[..16];
        return text;
    }

    public static bool IsUrl(string? icon)
    {
        var text = Normalize(icon);
        return text.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<byte[]?> LoadAsync(string key)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var path = Path.Combine(Folder, SafeFileName(key) + ".img");
            if (File.Exists(path))
            {
                var stored = await File.ReadAllBytesAsync(path);
                if (IsImage(stored))
                    return stored;
            }

            string url;
            if (IsUrl(key))
            {
                url = key;
            }
            else
            {
                var codepoints = ToTwemojiCodepoints(key);
                if (codepoints is null)
                    return null;
                url = "https://cdn.jsdelivr.net/gh/jdecked/twemoji@15.1.0/assets/72x72/" + codepoints + ".png";
            }

            using var response = await Http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return null;

            var bytes = await response.Content.ReadAsByteArrayAsync();
            if (!IsImage(bytes))
                return null;

            await File.WriteAllBytesAsync(path, bytes);
            return bytes;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string? ToTwemojiCodepoints(string emoji)
    {
        var parts = new List<string>();
        var enumerator = StringInfo.GetTextElementEnumerator(emoji);
        while (enumerator.MoveNext())
        {
            var element = enumerator.GetTextElement();
            foreach (var rune in element.EnumerateRunes())
            {
                // Twemoji omite el VS16 (fe0f) en muchos nombres de archivo.
                if (rune.Value == 0xFE0F)
                    continue;
                parts.Add(rune.Value.ToString("x", CultureInfo.InvariantCulture));
            }
        }

        return parts.Count == 0 ? null : string.Join('-', parts);
    }

    private static string SafeFileName(string key)
    {
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..24];
        return hash.ToLowerInvariant();
    }

    private static bool IsImage(byte[] bytes)
    {
        if (bytes.Length < 32)
            return false;
        if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            return true;
        if (bytes[0] == 0xFF && bytes[1] == 0xD8)
            return true;
        if (bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46)
            return true;
        return false;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SimTrade/1.0");
        return client;
    }
}
