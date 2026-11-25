using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using SimTrade.Controls;
using SimTrade.Services;

namespace SimTrade;

public partial class PriceChartWindow : Window
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("es-MX");
    private static PriceChartWindow? _dailyWindow;
    private static PriceChartWindow? _intradayWindow;

    private readonly YahooMarket _yahoo;
    private readonly bool _isIntraday;
    private CancellationTokenSource? _loadCts;
    private int _load;
    private string _symbol = "";
    private string _displayName = "";
    private bool _drawn;
    private IReadOnlyList<ChartPoint> _points = [];
    private double? _previousClose;

    private PriceChartWindow(YahooMarket yahoo, bool intraday)
    {
        _yahoo = yahoo;
        _isIntraday = intraday;
        InitializeComponent();
        PriceHeading.Text = intraday ? "Precio de hoy · MA20 / MA50" : "Precio diario · MA20 / MA50";
        PriceChart.HoverDateChanged += (_, mark) =>
        {
            RsiChart.LinkHover(mark);
            MacdChart.LinkHover(mark);
        };
        RsiChart.HoverDateChanged += (_, mark) =>
        {
            PriceChart.LinkHover(mark);
            MacdChart.LinkHover(mark);
        };
        MacdChart.HoverDateChanged += (_, mark) =>
        {
            PriceChart.LinkHover(mark);
            RsiChart.LinkHover(mark);
        };
    }

    public static void Present(string symbol, string? name, bool intraday, YahooMarket yahoo)
    {
        var window = intraday ? _intradayWindow : _dailyWindow;
        var owner = Application.Current?.MainWindow;
        if (window is null)
        {
            window = new PriceChartWindow(yahoo, intraday);
            if (owner is { IsLoaded: true })
                window.Owner = owner;
            if (intraday)
                _intradayWindow = window;
            else
                _dailyWindow = window;
            window.Show();
        }
        else if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
        window.Load(symbol, name);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var useDark = 1;
        DwmSetWindowAttribute(hwnd, 20, ref useDark, sizeof(int));
    }

    protected override void OnClosed(EventArgs e)
    {
        _loadCts?.Cancel();
        if (_isIntraday)
        {
            if (ReferenceEquals(_intradayWindow, this))
                _intradayWindow = null;
        }
        else if (ReferenceEquals(_dailyWindow, this))
        {
            _dailyWindow = null;
        }

        base.OnClosed(e);
    }

    private void Load(string symbol, string? name)
    {
        var generation = ++_load;
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;
        _symbol = symbol;
        _drawn = false;
        _displayName = DisplayName(name, symbol);
        ApplyHeading(symbol, _displayName);
        Status.Text = "Consultando Yahoo Finance…";
        QuotePanel.Visibility = Visibility.Collapsed;
        RsiPanel.Visibility = Visibility.Collapsed;
        RsiHeading.Text = "RSI (14)";
        PriceChart.FillKind = ChartFill.None;
        PriceChart.Samples = null;
        RsiChart.Samples = null;
        RsiChart.LowerGuide = RsiCalculator.BuyThreshold;
        RsiChart.UpperGuide = RsiCalculator.SellThreshold;
        _ = _isIntraday
            ? RefreshLoopAsync(symbol, generation, token)
            : LoadDailyAsync(symbol, generation, token);
    }

    private async Task RefreshLoopAsync(string symbol, int generation, CancellationToken cancellationToken)
    {
        try
        {
            await LoadIntradayAsync(symbol, generation, cancellationToken);
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (generation != _load)
                    return;
                await LoadIntradayAsync(symbol, generation, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task LoadIntradayAsync(string symbol, int generation, CancellationToken cancellationToken)
    {
        try
        {
            var series = await _yahoo.GetIntradaySeriesAsync(symbol, cancellationToken);
            if (cancellationToken.IsCancellationRequested || generation != _load)
                return;

            ApplyName(series.Name, symbol);
            var points = series.Points;
            if (points.Count < 2)
            {
                if (!_drawn)
                    Status.Text = "Todavía no hay precios de hoy para " + symbol + ".";
                return;
            }

            Draw(points, intraday: true, series.PreviousClose);
            var range = $"{points[0].When.ToString("HH:mm", Culture)} a {points[^1].When.ToString("HH:mm", Culture)}";
            var clock = DateTime.Now.ToString("HH:mm", Culture);
            var lastRsi = LastRsi(points);
            var volumeText = VolumeSummary(points);
            Status.Text = lastRsi is double
                ? $"Hoy {range} · actualizado a las {clock}{volumeText}"
                : $"Hoy {range} · aún no hay suficientes minutos para el RSI · actualizado a las {clock}{volumeText}";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            if (generation == _load)
                Status.Text = _drawn
                    ? "Yahoo Finance no respondió. Se vuelve a intentar en un minuto."
                    : "Yahoo Finance no respondió.";
        }
    }

    private async Task LoadDailyAsync(string symbol, int generation, CancellationToken cancellationToken)
    {
        try
        {
            var series = await _yahoo.GetDailySeriesAsync(symbol, cancellationToken);
            if (cancellationToken.IsCancellationRequested || generation != _load)
                return;

            ApplyName(series.Name, symbol);
            var points = series.Points;
            if (points.Count < 2)
            {
                Status.Text = "Yahoo Finance no publicó el historial diario de " + symbol + ".";
                return;
            }

            Draw(points, intraday: false, series.PreviousClose);
            var lastRsi = LastRsi(points);
            var range = $"{points[0].When.ToString("dd/MM/yyyy", Culture)} a {points[^1].When.ToString("dd/MM/yyyy", Culture)}";
            var volumeText = VolumeSummary(points);
            Status.Text = lastRsi is double
                ? $"Cierre {FormatPrice(points[^1].Close)} · {range}{volumeText}"
                : $"Cierre {FormatPrice(points[^1].Close)} · aún no hay suficientes cierres para el RSI · {range}{volumeText}";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            if (generation == _load)
                Status.Text = "Yahoo Finance no respondió.";
        }
    }

    private void Indicator_Changed(object sender, RoutedEventArgs e)
    {
        if (_points.Count >= 2)
            Draw(_points, _isIntraday, _previousClose);
    }

    private void Draw(IReadOnlyList<ChartPoint> points, bool intraday, double? previousClose)
    {
        _points = points;
        _previousClose = previousClose;
        var closes = points.Select(point => point.Close).ToArray();
        var rsi = RsiCalculator.Series(closes);
        var ma20 = IndicatorMath.Sma(closes, 20);
        var ma50 = IndicatorMath.Sma(closes, 50);
        var (macd, signal, _) = IndicatorMath.Macd(closes);
        var prices = new List<ChartSample>(points.Count);
        var oscillators = new List<ChartSample>(points.Count);
        var ma20Samples = new List<ChartSample>(points.Count);
        var ma50Samples = new List<ChartSample>(points.Count);
        var macdSamples = new List<ChartSample>(points.Count);
        var signalSamples = new List<ChartSample>(points.Count);
        for (var i = 0; i < points.Count; i++)
        {
            var day = DateOnly.FromDateTime(points[i].When);
            var stamp = intraday ? points[i].When : (DateTime?)null;
            prices.Add(new ChartSample(day, closes[i], stamp));
            oscillators.Add(new ChartSample(day, rsi[i], stamp));
            ma20Samples.Add(new ChartSample(day, ma20[i], stamp));
            ma50Samples.Add(new ChartSample(day, ma50[i], stamp));
            macdSamples.Add(new ChartSample(day, macd[i], stamp));
            signalSamples.Add(new ChartSample(day, signal[i], stamp));
        }

        // Intradía: variación vs cierre previo (igual que la tabla Portafolio).
        // Diario: variación del último cierre respecto al anterior.
        double baseline;
        if (intraday && previousClose is > 0)
            baseline = previousClose.Value;
        else
            baseline = closes[^2];

        var last = closes[^1];
        var change = last - baseline;
        ApplyMove(last, change, Math.Abs(baseline) > 0.0000001 ? change / baseline * 100d : null);
        PriceChart.Samples = prices;
        var overlays = new List<ChartOverlay>();
        if (ShowMa20.IsChecked == true)
            overlays.Add(new ChartOverlay("MA20", Freeze(Color.FromRgb(0x67, 0xE8, 0xF9)), ma20Samples));
        if (ShowMa50.IsChecked == true)
            overlays.Add(new ChartOverlay("MA50", Freeze(Color.FromRgb(0xFD, 0xE0, 0x68)), ma50Samples));
        PriceChart.Overlays = overlays.Count == 0 ? null : overlays;
        PriceHeading.Text = (intraday ? "Precio de hoy" : "Precio diario")
                            + (overlays.Count == 0 ? "" : " · " + string.Join(" / ", overlays.Select(item => item.Name)));

        RsiChart.Samples = oscillators;
        RsiChart.LowerGuide = RsiCalculator.BuyThreshold;
        RsiChart.UpperGuide = RsiCalculator.SellThreshold;
        RsiHeading.Text = $"RSI (14) · bajo {RsiCalculator.BuyThreshold:0} compra, sobre {RsiCalculator.SellThreshold:0} venta";
        ApplyRsi(rsi[^1]);

        var showMacd = ShowMacd.IsChecked == true && macd.Any(value => value is not null);
        MacdPanel.Visibility = showMacd ? Visibility.Visible : Visibility.Collapsed;
        MacdRow.Height = showMacd ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        if (showMacd)
        {
            MacdChart.Samples = macdSamples;
            MacdChart.Overlays = [new ChartOverlay("Señal", Freeze(Color.FromRgb(0xF9, 0xA8, 0xD4)), signalSamples)];
            MacdChart.LineBrush = Freeze(Color.FromRgb(0xA5, 0xB4, 0xFC));
            MacdChart.FillKind = ChartFill.None;
        }

        _drawn = true;
    }

    private static Brush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static double? LastRsi(IReadOnlyList<ChartPoint> points)
    {
        var rsi = RsiCalculator.Series(points.Select(point => point.Close).ToArray());
        return rsi.Length == 0 ? null : rsi[^1];
    }

    private static string VolumeSummary(IReadOnlyList<ChartPoint> points)
    {
        var last = points.LastOrDefault(point => point.Volume is > 0)?.Volume;
        if (last is null)
            return "";
        return " · vol. " + last.Value.ToString("N0", Culture);
    }

    private void ApplyMove(double price, double change, double? percent)
    {
        PriceText.Text = FormatPrice(price);
        ChangeAmountText.Text = FormatPrice(change);
        ChangePercentText.Text = percent is null
            ? ""
            : percent.Value.ToString("0.00", Culture) + "%";
        var brush = change switch
        {
            > 0 => (Brush)FindResource("UpBrush"),
            < 0 => (Brush)FindResource("DownBrush"),
            _ => (Brush)FindResource("TextBrush")
        };
        ChangeAmountText.Foreground = brush;
        ChangePercentText.Foreground = brush;
        PriceChart.FillKind = change switch
        {
            > 0 => ChartFill.Up,
            < 0 => ChartFill.Down,
            _ => ChartFill.Flat
        };
        PriceChart.LineBrush = brush;
        QuotePanel.Visibility = Visibility.Visible;
    }

    private void ApplyRsi(double? rsi)
    {
        if (rsi is not double value)
        {
            RsiPanel.Visibility = Visibility.Collapsed;
            return;
        }

        RsiValueText.Text = value.ToString("0.00", Culture);
        RsiSignalText.Text = RsiCalculator.Signal(value);
        RsiSignalText.Foreground = RsiCalculator.SignalKind(value) switch
        {
            "Buy" => (Brush)FindResource("UpBrush"),
            "Sell" => (Brush)FindResource("DownBrush"),
            _ => (Brush)FindResource("TextBrush")
        };
        RsiPanel.Visibility = Visibility.Visible;
    }

    private void ApplyName(string? seriesName, string symbol)
    {
        if (!string.IsNullOrEmpty(_displayName))
            return;
        _displayName = DisplayName(seriesName, symbol);
        ApplyHeading(symbol, _displayName);
    }

    private void ApplyHeading(string symbol, string name)
    {
        SymbolText.Text = symbol;
        SymbolLogo.Symbol = symbol;
        NameText.Text = name;
        NameText.Visibility = string.IsNullOrEmpty(name) ? Visibility.Collapsed : Visibility.Visible;
        var kind = _isIntraday ? "Gráfica intradía" : "Gráfica";
        Title = string.IsNullOrEmpty(name) ? kind + " · " + symbol : kind + " · " + symbol + " · " + name;
    }

    private static string DisplayName(string? name, string symbol)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "";
        var trimmed = name.Trim();
        if (trimmed is "—" or "-")
            return "";
        return string.Equals(trimmed, symbol, StringComparison.OrdinalIgnoreCase) ? "" : trimmed;
    }

    private static string FormatPrice(double value)
    {
        var abs = Math.Abs(value);
        if (abs >= 1)
            return value.ToString("N2", Culture);
        if (abs >= 0.01)
            return value.ToString("0.0000", Culture);
        return value.ToString("0.000000", Culture);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
