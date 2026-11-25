namespace SimTrade.Services;

public static class RsiCalculator
{
    public const int Period = 14;
    public const double BuyThreshold = 30;
    public const double SellThreshold = 70;

    public static double?[] Series(IReadOnlyList<double> closes, int period = Period)
    {
        var rsi = new double?[closes.Count];
        if (period < 1 || closes.Count <= period)
            return rsi;

        double gainSum = 0;
        double lossSum = 0;
        for (var i = 1; i <= period; i++)
        {
            var change = closes[i] - closes[i - 1];
            if (change > 0)
                gainSum += change;
            else
                lossSum -= change;
        }

        var averageGain = gainSum / period;
        var averageLoss = lossSum / period;
        rsi[period] = ToRsi(averageGain, averageLoss);

        for (var i = period + 1; i < closes.Count; i++)
        {
            var change = closes[i] - closes[i - 1];
            var gain = change > 0 ? change : 0;
            var loss = change < 0 ? -change : 0;
            averageGain = ((averageGain * (period - 1)) + gain) / period;
            averageLoss = ((averageLoss * (period - 1)) + loss) / period;
            rsi[i] = ToRsi(averageGain, averageLoss);
        }

        return rsi;
    }

    public static double? Latest(IReadOnlyList<double> closes, int period = Period)
    {
        var series = Series(closes, period);
        for (var index = series.Length - 1; index >= 0; index--)
        {
            if (series[index] is double value)
                return value;
        }

        return null;
    }

    public static string Signal(double? rsi)
    {
        if (rsi is null)
            return "—";
        if (rsi <= BuyThreshold)
            return "Compra";
        if (rsi >= SellThreshold)
            return "Venta";
        return "Neutral";
    }

    public static string SignalKind(double? rsi)
    {
        if (rsi is null)
            return "Unknown";
        if (rsi <= BuyThreshold)
            return "Buy";
        if (rsi >= SellThreshold)
            return "Sell";
        return "Neutral";
    }

    private static double? ToRsi(double averageGain, double averageLoss)
    {
        if (averageLoss <= 0)
            return averageGain <= 0 ? 50 : 100;

        var relativeStrength = averageGain / averageLoss;
        var rsi = 100d - (100d / (1d + relativeStrength));
        return double.IsFinite(rsi) ? rsi : null;
    }
}
