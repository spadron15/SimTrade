using System.Windows.Media;

namespace SimTrade.Services;

public sealed class ColumnSwatch
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required Brush Brush { get; init; }
    public bool IsNone => Key.Length == 0;
}

public static class ColumnColors
{
    public static IReadOnlyList<ColumnSwatch> Palette { get; } =
    [
        Swatch("", "Sin color", Colors.Transparent),
        Swatch("blue", "Azul", Color.FromRgb(0x1A, 0x33, 0x52)),
        Swatch("green", "Verde", Color.FromRgb(0x16, 0x3A, 0x2C)),
        Swatch("teal", "Cian", Color.FromRgb(0x14, 0x38, 0x40)),
        Swatch("violet", "Violeta", Color.FromRgb(0x32, 0x26, 0x48)),
        Swatch("rose", "Rosa", Color.FromRgb(0x40, 0x24, 0x30)),
        Swatch("amber", "Ámbar", Color.FromRgb(0x3A, 0x2E, 0x18)),
        Swatch("gray-light", "Gris claro", Color.FromRgb(0x4E, 0x58, 0x68)),
        Swatch("gray", "Gris", Color.FromRgb(0x3A, 0x42, 0x50)),
        Swatch("gray-dark", "Gris oscuro", Color.FromRgb(0x2A, 0x31, 0x3C))
    ];

    public static string? Normalize(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;

        return Palette.Any(swatch => string.Equals(swatch.Key, key, StringComparison.Ordinal))
            ? key
            : null;
    }

    public static Brush? BrushFor(string? key)
    {
        var normalized = Normalize(key);
        if (normalized is null)
            return null;

        return Palette.First(swatch => string.Equals(swatch.Key, normalized, StringComparison.Ordinal)).Brush;
    }

    private static ColumnSwatch Swatch(string key, string name, Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return new ColumnSwatch { Key = key, Name = name, Brush = brush };
    }
}
