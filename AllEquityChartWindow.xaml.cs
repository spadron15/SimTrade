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

public partial class AllEquityChartWindow : Window
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("es-MX");
    private static AllEquityChartWindow? _window;

    private static readonly Color[] Palette =
    [
        Color.FromRgb(0x67, 0xE8, 0xF9),
        Color.FromRgb(0x6E, 0xE7, 0xB7),
        Color.FromRgb(0xF9, 0xA8, 0xD4),
        Color.FromRgb(0xFD, 0xE0, 0x68),
        Color.FromRgb(0xA5, 0xB4, 0xFC),
        Color.FromRgb(0xFD, 0xBA, 0x74),
        Color.FromRgb(0xC8, 0x84, 0xFC),
        Color.FromRgb(0xF0, 0xAB, 0xFC)
    ];

    private TradeRepository? _repository;
    private YahooMarket? _yahoo;
    private IReadOnlyList<(long AccountId, string Name, string MarketKind)> _accounts = [];
    private Func<long, (decimal Cash, decimal Market, decimal Equity)>? _measure;
    private int _load;

    private AllEquityChartWindow()
    {
        InitializeComponent();
        BenchmarkBox.ItemsSource = Benchmarks.All;
        BenchmarkBox.SelectedIndex = 0;
    }

    public static void Present(
        TradeRepository repository,
        YahooMarket yahoo,
        IReadOnlyList<(long AccountId, string Name, string MarketKind)> accounts,
        Func<long, (decimal Cash, decimal Market, decimal Equity)> measure)
    {
        var owner = Application.Current?.MainWindow;
        if (_window is null)
        {
            _window = new AllEquityChartWindow();
            if (owner is { IsLoaded: true })
                _window.Owner = owner;
            _window.Show();
        }
        else if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _window.Activate();
        _window.Load(repository, yahoo, accounts, measure);
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
        TradeRepository repository,
        YahooMarket yahoo,
        IReadOnlyList<(long AccountId, string Name, string MarketKind)> accounts,
        Func<long, (decimal Cash, decimal Market, decimal Equity)> measure)
    {
        _repository = repository;
        _yahoo = yahoo;
        _accounts = accounts;
        _measure = measure;
        Status.Text = "Preparando historial…";
        Legend.ItemsSource = null;
        Chart.Series = null;
        SummaryText.Text = "";

        if (accounts.Count == 0)
        {
            Status.Text = "No hay portafolios para comparar.";
            return;
        }

        SeedToday(repository, accounts, measure);

        var seriesRaw = new List<(string Name, string MarketKind, IReadOnlyList<EquitySnapshotData> Snapshots)>();
        foreach (var account in accounts)
        {
            var snapshots = repository.ListEquitySnapshots(account.AccountId);
            if (snapshots.Count == 0)
                continue;
            seriesRaw.Add((account.Name, account.MarketKind, snapshots));
        }

        if (seriesRaw.Count == 0)
        {
            Status.Text = "Todavía no hay puntos de patrimonio para graficar.";
            return;
        }

        var dates = seriesRaw
            .SelectMany(item => item.Snapshots.Select(snap => snap.AsOfDate))
            .Distinct()
            .OrderBy(day => day)
            .ToList();
        if (dates.Count < 2)
        {
            Status.Text = "Se necesita al menos dos días de historial para comparar.";
            return;
        }

        var up = (Brush)FindResource("UpBrush");
        var down = (Brush)FindResource("DownBrush");
        var muted = (Brush)FindResource("MutedBrush");
        var lines = new List<MultiChartSeries>(seriesRaw.Count);
        var legend = new List<LegendItem>(seriesRaw.Count);

        for (var i = 0; i < seriesRaw.Count; i++)
        {
            var item = seriesRaw[i];
            var byDay = item.Snapshots.ToDictionary(snap => snap.AsOfDate, snap => snap.EquityMxn);
            var firstDay = item.Snapshots[0].AsOfDate;
            var firstEquity = item.Snapshots[0].EquityMxn;
            if (firstEquity == 0)
                continue;

            decimal? carry = null;
            var samples = new List<ChartSample>(dates.Count);
            foreach (var day in dates)
            {
                if (day < firstDay)
                {
                    samples.Add(new ChartSample(day, null));
                    continue;
                }

                if (byDay.TryGetValue(day, out var equity))
                    carry = equity;

                if (carry is null)
                {
                    samples.Add(new ChartSample(day, null));
                    continue;
                }

                var percent = (double)((carry.Value / firstEquity - 1m) * 100m);
                samples.Add(new ChartSample(day, percent));
            }

            var finite = samples.Where(sample => sample.Value is double).ToList();
            if (finite.Count < 2)
                continue;

            var last = finite[^1].Value!.Value;
            var stroke = Freeze(Palette[i % Palette.Length]);
            lines.Add(new MultiChartSeries(item.Name, stroke, samples));
            legend.Add(new LegendItem
            {
                Name = item.Name + " · " + MarketKinds.Label(item.MarketKind),
                Stroke = stroke,
                ChangeText = MxFormat.SignedPercent((decimal)last),
                ChangeBrush = last > 0 ? up : last < 0 ? down : muted
            });
        }

        if (lines.Count == 0)
        {
            Status.Text = "No hay suficientes puntos para comparar portafolios.";
            return;
        }

        Chart.Series = lines;
        Legend.ItemsSource = legend;
        SummaryText.Text = lines.Count == 1
            ? "1 portafolio"
            : lines.Count.ToString(Culture) + " portafolios";

        var from = dates[0].ToString("dd MMM yyyy", Culture);
        var to = dates[^1].ToString("dd MMM yyyy", Culture);
        Status.Text = dates.Count.ToString(Culture) + " días · " + from + " → " + to
                      + " · cada línea muestra el cambio % desde su primer día.";
        _ = AppendBenchmarkAsync(dates, from, to);
    }

    private void Benchmark_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_repository is null || _measure is null || _yahoo is null)
            return;
        Load(_repository, _yahoo, _accounts, _measure);
    }

    private async Task AppendBenchmarkAsync(IReadOnlyList<DateOnly> dates, string from, string to)
    {
        if (_yahoo is null || BenchmarkBox.SelectedItem is not BenchmarkOption bench
            || string.IsNullOrWhiteSpace(bench.Symbol))
            return;

        var generation = ++_load;
        try
        {
            var series = await _yahoo.GetDailySeriesAsync(bench.Symbol, CancellationToken.None);
            if (generation != _load || Chart.Series is null)
                return;

            var byDay = series.Points
                .GroupBy(point => DateOnly.FromDateTime(point.When))
                .ToDictionary(group => group.Key, group => group.Last().Close);
            double? first = null;
            var samples = new List<ChartSample>(dates.Count);
            foreach (var day in dates)
            {
                if (!byDay.TryGetValue(day, out var close) || close <= 0)
                {
                    samples.Add(new ChartSample(day, null));
                    continue;
                }

                first ??= close;
                samples.Add(new ChartSample(day, first is null or 0 ? 0 : (close / first.Value - 1d) * 100d));
            }

            if (samples.Count(sample => sample.Value is not null) < 2)
                return;

            var stroke = Freeze(Color.FromRgb(0xF4, 0xF1, 0xF8));
            var current = Chart.Series.ToList();
            current.Add(new MultiChartSeries(bench.Label, stroke, samples));
            Chart.Series = current;

            if (Legend.ItemsSource is IEnumerable<LegendItem> items)
            {
                var list = items.ToList();
                list.Add(new LegendItem
                {
                    Name = bench.Label,
                    Stroke = stroke,
                    ChangeText = samples.LastOrDefault(sample => sample.Value is not null)?.Value is double last
                        ? MxFormat.SignedPercent((decimal)last)
                        : "—",
                    ChangeBrush = (Brush)FindResource("MutedBrush")
                });
                Legend.ItemsSource = list;
            }

            Status.Text = dates.Count.ToString(Culture) + " días · " + from + " → " + to
                          + " · incluye " + bench.Label;
        }
        catch
        {
            if (generation == _load)
                Status.Text = dates.Count.ToString(Culture) + " días · " + from + " → " + to
                              + " · no se pudo cargar el benchmark.";
        }
    }

    private static void SeedToday(
        TradeRepository repository,
        IReadOnlyList<(long AccountId, string Name, string MarketKind)> accounts,
        Func<long, (decimal Cash, decimal Market, decimal Equity)> measure)
    {
        var zone = ResolveMexicoZone();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone));
        foreach (var account in accounts)
        {
            if (repository.HasEquitySnapshot(account.AccountId, today))
                continue;
            var current = measure(account.AccountId);
            repository.UpsertEquitySnapshot(
                account.AccountId, today, current.Equity, current.Market, current.Cash, overwrite: false);
        }
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

    private sealed class LegendItem
    {
        public required string Name { get; init; }
        public required Brush Stroke { get; init; }
        public required string ChangeText { get; init; }
        public required Brush ChangeBrush { get; init; }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
