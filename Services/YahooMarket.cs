using System.Net;
using System.Net.Http;
using System.Text.Json;
using SimTrade.Models;

namespace SimTrade.Services;

public sealed class YahooMarket : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate = new(4, 4);
    private readonly object _cacheLock = new();
    private readonly Dictionary<string, (decimal Rate, DateTime At)> _fxCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (double? Rsi, DateTime At)> _rsiCache = new(StringComparer.OrdinalIgnoreCase);

    public YahooMarket()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = true,
            CookieContainer = new CookieContainer()
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    public async Task<IReadOnlyList<SymbolHit>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var text = query.Trim();
        if (text.Length == 0)
            return [];

        var url = "https://query2.finance.yahoo.com/v1/finance/search?newsCount=0&quotesCount=12&enableFuzzyQuery=false&q="
                  + Uri.EscapeDataString(text);
        SearchResponse? payload;
        try
        {
            payload = await GetJsonAsync<SearchResponse>(url, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return [];
        }

        var hits = (payload?.Quotes ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item.Symbol))
            .Select(item => new SymbolHit
            {
                Symbol = item.Symbol!.Trim().ToUpperInvariant(),
                Name = First(item.Shortname, item.Longname, item.Symbol) ?? item.Symbol!,
                Exchange = First(item.ExchDisp, item.Exchange) ?? "",
                Kind = KindLabel(item.QuoteType)
            })
            .DistinctBy(item => item.Symbol)
            .ToList();

        var preferred = hits.Where(item => item.Kind is "Acción" or "ETF").ToList();
        return (preferred.Count > 0 ? preferred : hits.Where(item => item.Kind != "Derivado")).Take(8).ToList();
    }

    public async Task<PesosQuote?> PriceInPesosAsync(string symbol, CancellationToken cancellationToken)
    {
        var normalized = Normalize(symbol);
        if (normalized is null)
            return null;

        var raw = await GetRawAsync(normalized, cancellationToken);
        if (raw is null && !normalized.Contains('.') && !normalized.Contains('='))
            raw = await GetRawAsync(normalized + ".MX", cancellationToken);
        if (raw is null)
            return null;

        var converted = await ConvertAsync([raw], cancellationToken);
        return converted.FirstOrDefault();
    }

    public async Task<IReadOnlyList<PesosQuote>> PriceManyAsync(
        IEnumerable<string> symbols,
        CancellationToken cancellationToken)
    {
        var list = symbols
            .Select(Normalize)
            .Where(symbol => symbol is not null)
            .Select(symbol => symbol!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (list.Count == 0)
            return [];

        var raws = new List<RawQuote>();
        var fetched = await Task.WhenAll(list.Select(symbol => GetRawSafeAsync(symbol, cancellationToken)));
        foreach (var raw in fetched)
        {
            if (raw is not null)
                raws.Add(raw);
        }

        return await ConvertAsync(raws, cancellationToken);
    }

    public async Task<decimal?> GetUsdMxnAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await GetFxAsync("USD", cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    public void InvalidateFxCache()
    {
        lock (_cacheLock)
            _fxCache.Clear();
    }

    public void Dispose() => _http.Dispose();

    private async Task<IReadOnlyList<PesosQuote>> ConvertAsync(
        IReadOnlyList<RawQuote> raws,
        CancellationToken cancellationToken)
    {
        var result = new List<PesosQuote>(raws.Count);
        foreach (var raw in raws)
        {
            if (IsMexicanPeso(raw))
            {
                result.Add(ToPesos(raw, raw.Price, raw.Previous, null, inPesos: true));
                continue;
            }

            var rate = await GetFxAsync(raw.Currency, cancellationToken);
            if (rate is null or <= 0)
                continue;

            var fx = rate.Value;
            decimal? previous = raw.Previous is null ? null : decimal.Round(raw.Previous.Value * fx, 6, MidpointRounding.AwayFromZero);
            result.Add(ToPesos(raw, decimal.Round(raw.Price * fx, 6, MidpointRounding.AwayFromZero), previous, fx, inPesos: false));
        }

        return result;
    }

    private async Task<decimal?> GetFxAsync(string currency, CancellationToken cancellationToken)
    {
        if (currency.Equals("MXN", StringComparison.OrdinalIgnoreCase))
            return 1m;

        lock (_cacheLock)
        {
            if (_fxCache.TryGetValue(currency, out var cached) && DateTime.UtcNow - cached.At < TimeSpan.FromSeconds(60))
                return cached.Rate;
        }

        var pair = currency.Equals("USD", StringComparison.OrdinalIgnoreCase)
            ? "USDMXN=X"
            : currency.ToUpperInvariant() + "MXN=X";
        var quote = await GetRawAsync(pair, cancellationToken);
        if (quote is null || quote.Price <= 0)
            return null;

        var rate = decimal.Round(quote.Price, 6, MidpointRounding.AwayFromZero);
        lock (_cacheLock)
            _fxCache[currency] = (rate, DateTime.UtcNow);
        return rate;
    }

    private async Task<RawQuote?> GetRawSafeAsync(string symbol, CancellationToken cancellationToken)
    {
        try
        {
            return await GetRawAsync(symbol, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private async Task<RawQuote?> GetRawAsync(string symbol, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var url = "https://query1.finance.yahoo.com/v8/finance/chart/"
                      + Uri.EscapeDataString(symbol)
                      + "?range=5d&interval=1d";
            ChartEnvelope? payload;
            try
            {
                payload = await GetJsonAsync<ChartEnvelope>(url, cancellationToken);
            }
            catch (HttpRequestException)
            {
                return null;
            }

            var series = payload?.Chart?.Result?.FirstOrDefault();
            var meta = series?.Meta;
            if (meta?.RegularMarketPrice is not > 0 || series is null)
                return null;

            var resolved = Normalize(meta.Symbol) ?? symbol;
            var rawCurrency = (meta.Currency ?? "").Trim();
            var currency = rawCurrency.ToUpperInvariant();
            if (currency.Length == 0 && resolved.EndsWith(".MX", StringComparison.OrdinalIgnoreCase))
                currency = "MXN";

            var price = (decimal)meta.RegularMarketPrice.Value;
            // chartPreviousClose es el primer cierre del rango (p. ej. hace 5 días), no el de ayer.
            var previousValue = meta.RegularMarketPreviousClose ?? meta.PreviousClose;
            decimal? previous = previousValue is > 0
                ? (decimal)previousValue.Value
                : ReadPreviousClose(series, price);
            decimal? sessionOpen = TryRegularWindow(meta, out var sessionStart, out var sessionEnd)
                ? ReadSessionOpen(series, sessionStart, sessionEnd)
                : null;
            decimal? dayLow = Positive(meta.RegularMarketDayLow);
            decimal? dayHigh = Positive(meta.RegularMarketDayHigh);
            decimal? weekLow = Positive(meta.FiftyTwoWeekLow);
            decimal? weekHigh = Positive(meta.FiftyTwoWeekHigh);
            if (rawCurrency.Equals("GBp", StringComparison.Ordinal) || rawCurrency.Equals("GBX", StringComparison.OrdinalIgnoreCase))
            {
                currency = "GBP";
                price /= 100m;
                if (previous is not null)
                    previous /= 100m;
                if (sessionOpen is not null)
                    sessionOpen /= 100m;
                if (dayLow is not null)
                    dayLow /= 100m;
                if (dayHigh is not null)
                    dayHigh /= 100m;
                if (weekLow is not null)
                    weekLow /= 100m;
                if (weekHigh is not null)
                    weekHigh /= 100m;
            }

            return new RawQuote(
                resolved,
                First(meta.ShortName, meta.LongName, resolved) ?? resolved,
                First(meta.FullExchangeName, meta.ExchangeName) ?? "",
                currency,
                meta.InstrumentType ?? "",
                price,
                previous,
                IsRegularOpen(meta),
                sessionOpen,
                dayLow,
                dayHigh,
                weekLow,
                weekHigh);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<T?> GetJsonAsync<T>(string url, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return default;
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }

    private static PesosQuote ToPesos(RawQuote raw, decimal priceMxn, decimal? previousMxn, decimal? fx, bool inPesos)
    {
        decimal? ToMxn(decimal? value) =>
            value is null
                ? null
                : decimal.Round(inPesos ? value.Value : value.Value * fx.GetValueOrDefault(), 6, MidpointRounding.AwayFromZero);

        return new()
        {
            Symbol = raw.Symbol,
            CompanyName = raw.CompanyName,
            Exchange = raw.Exchange,
            Currency = string.IsNullOrWhiteSpace(raw.Currency) ? "MXN" : raw.Currency,
            Instrument = raw.Instrument,
            PriceOriginal = decimal.Round(raw.Price, 6, MidpointRounding.AwayFromZero),
            PreviousOriginal = raw.Previous is null ? null : decimal.Round(raw.Previous.Value, 6, MidpointRounding.AwayFromZero),
            PriceMxn = priceMxn,
            PreviousMxn = previousMxn,
            FxRate = fx,
            InPesos = inPesos,
            MarketOpen = raw.MarketOpen,
            FetchedAtUtc = DateTime.UtcNow,
            IsStale = false,
            SessionOpenOriginal = raw.SessionOpen is null
                ? null
                : decimal.Round(raw.SessionOpen.Value, 6, MidpointRounding.AwayFromZero),
            SessionOpenMxn = ToMxn(raw.SessionOpen),
            DayLowOriginal = RoundOpt(raw.DayLow),
            DayHighOriginal = RoundOpt(raw.DayHigh),
            DayLowMxn = ToMxn(raw.DayLow),
            DayHighMxn = ToMxn(raw.DayHigh),
            FiftyTwoWeekLowOriginal = RoundOpt(raw.FiftyTwoWeekLow),
            FiftyTwoWeekHighOriginal = RoundOpt(raw.FiftyTwoWeekHigh),
            FiftyTwoWeekLowMxn = ToMxn(raw.FiftyTwoWeekLow),
            FiftyTwoWeekHighMxn = ToMxn(raw.FiftyTwoWeekHigh)
        };
    }

    private static decimal? Positive(double? value) =>
        value is > 0 ? (decimal)value.Value : null;

    private static decimal? RoundOpt(decimal? value) =>
        value is null ? null : decimal.Round(value.Value, 6, MidpointRounding.AwayFromZero);

    private static bool TryRegularWindow(ChartMeta meta, out long start, out long end)
    {
        start = 0;
        end = 0;
        var regular = meta.CurrentTradingPeriod?.Regular;
        if (regular?.Start is not long sessionStart || regular.End is not long sessionEnd || sessionEnd <= sessionStart)
            return false;
        start = sessionStart;
        end = sessionEnd;
        return true;
    }

    private static bool IsRegularOpen(ChartMeta meta)
    {
        if (!TryRegularWindow(meta, out var start, out var end))
            return false;
        // TEMP_FORCE_CLOSED: remove after queue UI check
        if (string.Equals(Environment.GetEnvironmentVariable("SIMTRADE_FORCE_CLOSED"), "1", StringComparison.Ordinal))
            return false;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return now >= start && now < end;
    }

    private static decimal? ReadSessionOpen(ChartResult series, long start, long end)
    {
        var stamps = series.Timestamp;
        var opens = series.Indicators?.Quote?.FirstOrDefault()?.Open;
        if (stamps is null || opens is null)
            return null;

        for (var index = stamps.Count - 1; index >= 0; index--)
        {
            if (stamps[index] is not long stamp || stamp < start || stamp >= end)
                continue;
            if (index < opens.Count && opens[index] is double value && value > 0)
                return (decimal)value;
        }

        return null;
    }

    private static decimal? ReadPreviousClose(ChartResult series, decimal currentPrice)
    {
        var closes = series.Indicators?.Quote?.FirstOrDefault()?.Close;
        if (closes is null || closes.Count == 0)
            return null;

        var skipCurrent = false;
        for (var index = closes.Count - 1; index >= 0; index--)
        {
            if (closes[index] is not double value || value <= 0)
                continue;

            var close = (decimal)value;
            if (!skipCurrent && AlmostSamePrice(close, currentPrice))
            {
                skipCurrent = true;
                continue;
            }

            return close;
        }

        return null;
    }

    private static bool AlmostSamePrice(decimal left, decimal right)
    {
        var scale = Math.Max(Math.Abs(left), Math.Abs(right));
        if (scale == 0)
            return true;
        return Math.Abs(left - right) <= Math.Max(0.0001m, scale * 0.0005m);
    }

    private static bool IsMexicanPeso(RawQuote raw) =>
        raw.Currency.Equals("MXN", StringComparison.OrdinalIgnoreCase)
        || (raw.Currency.Length == 0 && raw.Symbol.EndsWith(".MX", StringComparison.OrdinalIgnoreCase));

    public static string? Normalize(string? symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return null;
        var text = symbol.Trim().ToUpperInvariant();
        if (text.Length is 0 or > 24)
            return null;
        foreach (var character in text)
        {
            var ok = character is (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '.' or '-' or '=' or '^';
            if (!ok)
                return null;
        }
        return text;
    }

    private static string KindLabel(string? quoteType) => quoteType?.ToUpperInvariant() switch
    {
        "EQUITY" => "Acción",
        "ETF" => "ETF",
        "MUTUALFUND" => "Fondo",
        "OPTION" or "FUTURE" or "FUTURES" => "Derivado",
        "INDEX" => "Índice",
        "CURRENCY" => "Divisa",
        _ => "Otro"
    };

    private static string? First(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }
        return null;
    }

    private sealed record RawQuote(
        string Symbol,
        string CompanyName,
        string Exchange,
        string Currency,
        string Instrument,
        decimal Price,
        decimal? Previous,
        bool MarketOpen,
        decimal? SessionOpen,
        decimal? DayLow,
        decimal? DayHigh,
        decimal? FiftyTwoWeekLow,
        decimal? FiftyTwoWeekHigh);

    private sealed class SearchResponse
    {
        public List<SearchQuote>? Quotes { get; set; }
    }

    private sealed class SearchQuote
    {
        public string? Symbol { get; set; }
        public string? Shortname { get; set; }
        public string? Longname { get; set; }
        public string? QuoteType { get; set; }
        public string? ExchDisp { get; set; }
        public string? Exchange { get; set; }
    }

    private sealed class ChartEnvelope
    {
        public ChartBody? Chart { get; set; }
    }

    private sealed class ChartBody
    {
        public List<ChartResult>? Result { get; set; }
    }

    private sealed class ChartResult
    {
        public ChartMeta? Meta { get; set; }
        public List<long?>? Timestamp { get; set; }
        public ChartIndicators? Indicators { get; set; }
    }

    private sealed class ChartIndicators
    {
        public List<ChartQuoteValues>? Quote { get; set; }
        public List<ChartAdjustedValues>? Adjclose { get; set; }
    }

    private sealed class ChartQuoteValues
    {
        public List<double?>? Open { get; set; }
        public List<double?>? Close { get; set; }
        public List<double?>? Volume { get; set; }
    }

    private sealed class ChartAdjustedValues
    {
        public List<double?>? Adjclose { get; set; }
    }

    private sealed class ChartMeta
    {
        public string? Symbol { get; set; }
        public string? ShortName { get; set; }
        public string? LongName { get; set; }
        public string? Currency { get; set; }
        public string? ExchangeName { get; set; }
        public string? FullExchangeName { get; set; }
        public string? InstrumentType { get; set; }
        public double? RegularMarketPrice { get; set; }
        public double? PreviousClose { get; set; }
        public double? ChartPreviousClose { get; set; }
        public double? RegularMarketPreviousClose { get; set; }
        public double? RegularMarketTime { get; set; }
        public double? RegularMarketDayHigh { get; set; }
        public double? RegularMarketDayLow { get; set; }
        public double? FiftyTwoWeekHigh { get; set; }
        public double? FiftyTwoWeekLow { get; set; }
        public ChartTradingPeriod? CurrentTradingPeriod { get; set; }
    }

    private sealed class ChartTradingPeriod
    {
        public ChartSessionWindow? Regular { get; set; }
    }

    private sealed class ChartSessionWindow
    {
        public long? Start { get; set; }
        public long? End { get; set; }
    }

    public async Task<ChartSeries> GetDailySeriesAsync(string symbol, CancellationToken cancellationToken)
    {
        var normalized = Normalize(symbol);
        if (normalized is null)
            return ChartSeries.Empty;

        var url = "https://query1.finance.yahoo.com/v8/finance/chart/"
                  + Uri.EscapeDataString(normalized)
                  + "?range=5y&interval=1d&includeAdjustedClose=true";
        return await ReadSeriesAsync(url, intraday: false, cancellationToken);
    }

    public async Task<double?> GetDailyRsiAsync(string symbol, CancellationToken cancellationToken)
    {
        var normalized = Normalize(symbol);
        if (normalized is null)
            return null;

        lock (_cacheLock)
        {
            if (_rsiCache.TryGetValue(normalized, out var cached)
                && DateTime.UtcNow - cached.At < TimeSpan.FromMinutes(10))
                return cached.Rsi;
        }

        var url = "https://query1.finance.yahoo.com/v8/finance/chart/"
                  + Uri.EscapeDataString(normalized)
                  + "?range=6mo&interval=1d&includeAdjustedClose=true";
        var series = await ReadSeriesAsync(url, intraday: false, cancellationToken);
        var closes = series.Points.Select(point => point.Close).ToArray();
        var rsi = RsiCalculator.Latest(closes);
        lock (_cacheLock)
            _rsiCache[normalized] = (rsi, DateTime.UtcNow);
        return rsi;
    }

    public async Task<IReadOnlyDictionary<string, double?>> GetDailyRsiManyAsync(
        IEnumerable<string> symbols,
        CancellationToken cancellationToken)
    {
        var list = symbols
            .Select(Normalize)
            .Where(symbol => symbol is not null)
            .Select(symbol => symbol!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (list.Count == 0)
            return new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase);

        var pairs = await Task.WhenAll(list.Select(async symbol =>
        {
            try
            {
                return (Symbol: symbol, Rsi: await GetDailyRsiAsync(symbol, cancellationToken));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return (Symbol: symbol, Rsi: (double?)null);
            }
        }));

        return pairs.ToDictionary(pair => pair.Symbol, pair => pair.Rsi, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<ChartSeries> GetIntradaySeriesAsync(string symbol, CancellationToken cancellationToken)
    {
        var normalized = Normalize(symbol);
        if (normalized is null)
            return ChartSeries.Empty;

        var start = new DateTimeOffset(DateTime.Today);
        var end = DateTimeOffset.Now.AddMinutes(1);
        var url = "https://query1.finance.yahoo.com/v8/finance/chart/"
                  + Uri.EscapeDataString(normalized)
                  + $"?period1={start.ToUnixTimeSeconds()}&period2={end.ToUnixTimeSeconds()}&interval=1m&includePrePost=false";
        return await ReadSeriesAsync(url, intraday: true, cancellationToken);
    }

    private async Task<ChartSeries> ReadSeriesAsync(string url, bool intraday, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ChartEnvelope? payload;
            try
            {
                payload = await GetJsonAsync<ChartEnvelope>(url, cancellationToken);
            }
            catch (HttpRequestException)
            {
                return ChartSeries.Empty;
            }

            var result = payload?.Chart?.Result?.FirstOrDefault();
            var meta = result?.Meta;
            var name = First(meta?.ShortName, meta?.LongName) ?? "";
            var points = intraday ? MapIntraday(result) : MapDaily(result);
            var scale = PriceScale(meta);
            double? previous = null;
            var previousValue = meta?.RegularMarketPreviousClose ?? meta?.PreviousClose;
            if (previousValue is > 0)
                previous = previousValue.Value * scale;
            return new ChartSeries(name, points, previous);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static List<ChartPoint> MapDaily(ChartResult? series)
    {
        var timestamps = series?.Timestamp;
        if (timestamps is null || timestamps.Count == 0)
            return [];

        var quote = series?.Indicators?.Quote?.FirstOrDefault();
        var closes = quote?.Close;
        var volumes = quote?.Volume;
        var adjusted = series?.Indicators?.Adjclose?.FirstOrDefault()?.Adjclose;
        var scale = PriceScale(series?.Meta);
        var byDay = new SortedDictionary<DateOnly, (double Close, double? Volume)>();
        for (var i = 0; i < timestamps.Count; i++)
        {
            var stamp = timestamps[i];
            if (stamp is null or <= 0)
                continue;

            double? price = null;
            if (adjusted is not null && i < adjusted.Count && adjusted[i] is > 0)
                price = adjusted[i];
            else if (closes is not null && i < closes.Count && closes[i] is > 0)
                price = closes[i];
            if (price is null)
                continue;

            double? volume = volumes is not null && i < volumes.Count ? volumes[i] : null;
            var day = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(stamp.Value).UtcDateTime);
            byDay[day] = (price.Value * scale, volume);
        }

        return byDay.Select(pair => new ChartPoint(pair.Key.ToDateTime(TimeOnly.MinValue), pair.Value.Close, false, pair.Value.Volume)).ToList();
    }

    private static List<ChartPoint> MapIntraday(ChartResult? series)
    {
        var timestamps = series?.Timestamp;
        var quote = series?.Indicators?.Quote?.FirstOrDefault();
        var closes = quote?.Close;
        var volumes = quote?.Volume;
        if (timestamps is null || closes is null)
            return [];

        var today = DateTime.Today;
        var scale = PriceScale(series?.Meta);
        var points = new List<ChartPoint>();
        for (var i = 0; i < timestamps.Count && i < closes.Count; i++)
        {
            var stamp = timestamps[i];
            var close = closes[i];
            if (stamp is null or <= 0 || close is not > 0)
                continue;

            var local = DateTimeOffset.FromUnixTimeSeconds(stamp.Value).LocalDateTime;
            if (local.Date != today)
                continue;

            var minute = new DateTime(local.Year, local.Month, local.Day, local.Hour, local.Minute, 0);
            var price = close.Value * scale;
            double? volume = volumes is not null && i < volumes.Count ? volumes[i] : null;
            if (points.Count > 0 && points[^1].When == minute)
                points[^1] = new ChartPoint(minute, price, true, volume);
            else
                points.Add(new ChartPoint(minute, price, true, volume));
        }

        return points;
    }

    private static double PriceScale(ChartMeta? meta)
    {
        var currency = meta?.Currency ?? "";
        return currency.Equals("GBp", StringComparison.Ordinal) || currency.Equals("GBX", StringComparison.OrdinalIgnoreCase)
            ? 0.01
            : 1;
    }
}

public sealed record ChartPoint(DateTime When, double Close, bool Intraday, double? Volume = null);

public sealed record ChartSeries(string Name, IReadOnlyList<ChartPoint> Points, double? PreviousClose = null)
{
    public static ChartSeries Empty { get; } = new("", [], null);
}
