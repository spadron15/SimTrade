namespace SimTrade.Services;

public static class IndicatorMath
{
    public static double?[] Sma(IReadOnlyList<double> values, int period)
    {
        var result = new double?[values.Count];
        if (period < 1 || values.Count < period)
            return result;

        double sum = 0;
        for (var i = 0; i < values.Count; i++)
        {
            sum += values[i];
            if (i >= period)
                sum -= values[i - period];
            if (i >= period - 1)
                result[i] = sum / period;
        }

        return result;
    }

    public static double?[] Ema(IReadOnlyList<double> values, int period)
    {
        var result = new double?[values.Count];
        if (period < 1 || values.Count < period)
            return result;

        double sum = 0;
        for (var i = 0; i < period; i++)
            sum += values[i];
        var ema = sum / period;
        result[period - 1] = ema;
        var alpha = 2d / (period + 1);
        for (var i = period; i < values.Count; i++)
        {
            ema = (values[i] - ema) * alpha + ema;
            result[i] = ema;
        }

        return result;
    }

    public static (double?[] Macd, double?[] Signal, double?[] Histogram) Macd(
        IReadOnlyList<double> closes,
        int fast = 12,
        int slow = 26,
        int signal = 9)
    {
        var macd = new double?[closes.Count];
        var signalLine = new double?[closes.Count];
        var histogram = new double?[closes.Count];
        if (closes.Count < slow + signal)
            return (macd, signalLine, histogram);

        var fastEma = Ema(closes, fast);
        var slowEma = Ema(closes, slow);
        var macdValues = new List<double>();
        var macdIndex = new List<int>();
        for (var i = 0; i < closes.Count; i++)
        {
            if (fastEma[i] is not double f || slowEma[i] is not double s)
                continue;
            var value = f - s;
            macd[i] = value;
            macdValues.Add(value);
            macdIndex.Add(i);
        }

        if (macdValues.Count < signal)
            return (macd, signalLine, histogram);

        var signalSeries = Ema(macdValues, signal);
        for (var i = 0; i < macdValues.Count; i++)
        {
            if (signalSeries[i] is not double sig)
                continue;
            var index = macdIndex[i];
            signalLine[index] = sig;
            if (macd[index] is double m)
                histogram[index] = m - sig;
        }

        return (macd, signalLine, histogram);
    }
}
