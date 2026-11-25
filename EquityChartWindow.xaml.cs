using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using SimTrade.Controls;
using SimTrade.Data;
using SimTrade.Models;
using SimTrade.Services;

namespace SimTrade;

public partial class EquityChartWindow : Window
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("es-MX");
    private static EquityChartWindow? _window;

    private TradeRepository? _repository;
    private YahooMarket? _yahoo;
    private long _accountId;
    private string _accountName = "";
    private string _marketKind = "";
    private Func<(decimal Cash, decimal Market, decimal Equity)>? _measure;
    private IReadOnlyList<EquitySnapshotData> _snapshots = [];
    private int _load;

    private EquityChartWindow()
    {
        InitializeComponent();
        BenchmarkBox.ItemsSource = Benchmarks.All;
        BenchmarkBox.SelectedIndex = 0;
    }

    public static void Present(
        long accountId,
        string accountName,
        string marketKind,
        TradeRepository repository,
        YahooMarket yahoo,
        Func<(decimal Cash, decimal Market, decimal Equity)> measure)
    {
        var owner = Application.Current?.MainWindow;
        if (_window is null)
        {
            _window = new EquityChartWindow();
            if (owner is { IsLoaded: true })
                _window.Owner = owner;
            _window.Show();
        }
        else if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _window.Activate();
        _window.Load(accountId, accountName, marketKind, repository, yahoo, measure);
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
        if (ReferenceEquals(_window, this))
            _window = null;
        base.OnClosed(e);
    }

    private void Load(
        long accountId,
        string accountName,
        string marketKind,
        TradeRepository repository,
        YahooMarket yahoo,
        Func<(decimal Cash, decimal Market, decimal Equity)> measure)
    {
        _repository = repository;
        _yahoo = yahoo;
        _accountId = accountId;
        _accountName = accountName;
        _marketKind = marketKind;
        _measure = measure;

        TitleText.Text = accountName;
        SubtitleText.Text = MarketKinds.Label(marketKind) + " · evolución diaria del patrimonio";
        Status.Text = "Preparando historial…";

        var account = repository.GetAccount(accountId);
        if (account is null)
        {
            Status.Text = "No se encontró la cuenta.";
            EquityChart.Samples = null;
            EquityChart.Overlays = null;
            return;
        }

        var current = measure();
        SeedHistory(repository, account, current);
        _snapshots = repository.ListEquitySnapshots(accountId);
        _ = RenderAsync();
    }

    private async void Benchmark_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_repository is null)
            return;
        await RenderAsync();
    }

    private async Task RenderAsync()
    {
        var generation = ++_load;
        var snapshots = _snapshots;
        if (snapshots.Count == 0)
        {
            Status.Text = "Todavía no hay puntos para graficar.";
            EquityChart.Samples = null;
            EquityChart.Overlays = null;
            EquityText.Text = _measure is null ? "" : MxFormat.Money(_measure().Equity);
            ChangeText.Text = "";
            return;
        }

        var usePercent = BenchmarkBox.SelectedItem is BenchmarkOption option && !string.IsNullOrWhiteSpace(option.Symbol);
        var firstEquity = snapshots[0].EquityMxn;
        var samples = snapshots.Select(item =>
        {
            double value = usePercent && firstEquity != 0
                ? (double)((item.EquityMxn / firstEquity - 1m) * 100m)
                : (double)item.EquityMxn;
            return new ChartSample(item.AsOfDate, value);
        }).ToList();

        EquityChart.Samples = samples;
        EquityChart.ValueFormat = usePercent ? "+0.00;-0.00;0.00" : "C0";
        EquityChart.Overlays = null;
        BenchmarkLegend.Text = "";

        var first = snapshots[0].EquityMxn;
        var last = snapshots[^1].EquityMxn;
        var change = last - first;
        var percent = first == 0 ? 0 : change / first * 100m;
        EquityText.Text = usePercent ? MxFormat.SignedPercent(percent) : MxFormat.Money(last);
        ChangeText.Text = usePercent
            ? ""
            : MxFormat.SignedMoney(change) + "  " + MxFormat.SignedPercent(percent);

        if (change > 0)
        {
            EquityChart.LineBrush = (Brush)FindResource("UpBrush");
            EquityChart.FillKind = ChartFill.Up;
            ChangeText.Foreground = (Brush)FindResource("UpBrush");
        }
        else if (change < 0)
        {
            EquityChart.LineBrush = (Brush)FindResource("DownBrush");
            EquityChart.FillKind = ChartFill.Down;
            ChangeText.Foreground = (Brush)FindResource("DownBrush");
        }
        else
        {
            EquityChart.LineBrush = (Brush)FindResource("MutedBrush");
            EquityChart.FillKind = ChartFill.Flat;
            ChangeText.Foreground = (Brush)FindResource("MutedBrush");
        }

        var from = snapshots[0].AsOfDate.ToString("dd MMM yyyy", Culture);
        var to = snapshots[^1].AsOfDate.ToString("dd MMM yyyy", Culture);
        Status.Text = snapshots.Count == 1
            ? "1 día de historial · " + to
            : snapshots.Count.ToString(Culture) + " días · " + from + " → " + to;

        if (!usePercent || _yahoo is null || BenchmarkBox.SelectedItem is not BenchmarkOption bench
            || string.IsNullOrWhiteSpace(bench.Symbol))
            return;

        Status.Text += " · cargando " + bench.Label + "…";
        try
        {
            var series = await _yahoo.GetDailySeriesAsync(bench.Symbol, CancellationToken.None);
            if (generation != _load)
                return;
            var overlay = AlignBenchmark(snapshots, series.Points);
            if (overlay.Count(sample => sample.Value is not null) < 2)
            {
                Status.Text = snapshots.Count.ToString(Culture) + " días · " + from + " → " + to
                              + " · sin datos suficientes de " + bench.Label;
                return;
            }

            var stroke = Freeze(Color.FromRgb(0xA5, 0xB4, 0xFC));
            EquityChart.Overlays = [new ChartOverlay(bench.Label, stroke, overlay)];
            EquityChart.FillKind = ChartFill.None;
            BenchmarkLegend.Text = "Línea punteada: " + bench.Label + " (%)";
            Status.Text = snapshots.Count.ToString(Culture) + " días · " + from + " → " + to
                          + " · rendimiento % vs " + bench.Label;
        }
        catch
        {
            if (generation == _load)
                Status.Text = snapshots.Count.ToString(Culture) + " días · " + from + " → " + to
                              + " · no se pudo cargar el benchmark.";
        }
    }

    private static List<ChartSample> AlignBenchmark(
        IReadOnlyList<EquitySnapshotData> snapshots,
        IReadOnlyList<ChartPoint> points)
    {
        var byDay = points
            .GroupBy(point => DateOnly.FromDateTime(point.When))
            .ToDictionary(group => group.Key, group => group.Last().Close);
        double? first = null;
        var samples = new List<ChartSample>(snapshots.Count);
        foreach (var snap in snapshots)
        {
            if (!byDay.TryGetValue(snap.AsOfDate, out var close) || close <= 0)
            {
                samples.Add(new ChartSample(snap.AsOfDate, null));
                continue;
            }

            first ??= close;
            var percent = first is null or 0 ? 0 : (close / first.Value - 1d) * 100d;
            samples.Add(new ChartSample(snap.AsOfDate, percent));
        }

        return samples;
    }

    private static void SeedHistory(
        TradeRepository repository,
        AccountData account,
        (decimal Cash, decimal Market, decimal Equity) current)
    {
        var zone = ResolveMexicoZone();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone));
        if (!repository.HasEquitySnapshot(account.Id, today))
            repository.UpsertEquitySnapshot(account.Id, today, current.Equity, current.Market, current.Cash, overwrite: false);
    }

    private static Brush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static TimeZoneInfo ResolveMexicoZone()
    {
        foreach (var id in new[] { "America/Mexico_City", "Central Standard Time (Mexico)", "Central Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.Local;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
