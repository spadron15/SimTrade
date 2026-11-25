using System.Globalization;

namespace SimTrade.Services;

public static class MxFormat
{
    public static readonly CultureInfo Culture = new("es-MX");

    /// <summary>Importe en pesos sin símbolo de moneda ni signo +.</summary>
    public static string Money(decimal value) =>
        value.ToString("#,##0.00", Culture);

    public static string SignedMoney(decimal value)
    {
        var text = Math.Abs(value).ToString("#,##0.00", Culture);
        if (value < 0)
            return "-" + text;
        return text;
    }

    public static string Price(decimal value)
    {
        var rounded = decimal.Round(value, 4, MidpointRounding.AwayFromZero);
        var cents = decimal.Round(rounded, 2, MidpointRounding.AwayFromZero);
        return rounded == cents
            ? rounded.ToString("#,##0.00", Culture)
            : rounded.ToString("#,##0.0000", Culture);
    }

    public static string Plain(decimal value) =>
        value.ToString("#,##0.00##", Culture);

    public static string Fx(decimal value) =>
        value.ToString("#,##0.0000", Culture);

    public static string Quantity(decimal value) =>
        value.ToString("#,##0.########", Culture);

    public static string Percent(decimal value) =>
        value.ToString("0.00", Culture) + "%";

    public static string PercentInput(decimal value) =>
        value.ToString("0.####", Culture);

    public static string Since(DateTime created)
    {
        var local = created.ToLocalTime();
        var elapsed = DateTime.Now - local;
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;

        return "Desde el " + local.ToString("dd MMM yyyy", Culture) + " · " + Age(elapsed);
    }

    public static string Age(TimeSpan elapsed)
    {
        if (elapsed.TotalHours < 1)
            return "hace menos de una hora";

        if (elapsed.TotalDays < 1)
        {
            var hours = Math.Max(1, (int)elapsed.TotalHours);
            return hours == 1 ? "hace 1 hora" : "hace " + hours.ToString(Culture) + " horas";
        }

        var days = Math.Max(1, (int)elapsed.TotalDays);
        return days == 1 ? "hace 1 día" : "hace " + days.ToString(Culture) + " días";
    }

    public static string SignedPercent(decimal value)
    {
        var text = Math.Abs(value).ToString("0.00", Culture) + "%";
        if (value < 0)
            return "-" + text;
        return text;
    }

    public static bool TryParse(string? text, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var cleaned = text.Trim()
            .Replace("MXN", "", StringComparison.OrdinalIgnoreCase)
            .Replace("$", "", StringComparison.Ordinal)
            .Replace("+", "", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal);

        if (decimal.TryParse(cleaned, NumberStyles.Number, Culture, out value))
            return true;
        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }
}
