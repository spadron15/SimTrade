using SimTrade.Models;

namespace SimTrade.Services;

public sealed record MonthPnl(int Year, int Month, decimal RealizedPnl)
{
    public string Label => new DateTime(Year, Month, 1).ToString("MMM yyyy", MxFormat.Culture);
}

public sealed record TradingStatsResult(
    int ClosedTrades,
    int Wins,
    int Losses,
    decimal WinRatePercent,
    decimal AverageWin,
    decimal AverageLoss,
    decimal AveragePnl,
    decimal TotalRealized,
    decimal MaxDrawdownPercent,
    decimal MaxDrawdownMoney,
    MonthPnl? BestMonth,
    MonthPnl? WorstMonth);

public static class TradingStats
{
    public static TradingStatsResult Compute(
        IReadOnlyList<TradeData> trades,
        IReadOnlyList<EquitySnapshotData> equity)
    {
        var sells = trades
            .Where(trade => trade.Side.Equals("SELL", StringComparison.OrdinalIgnoreCase)
                            && trade.RealizedPnlMxn is not null)
            .ToList();

        var wins = sells.Where(trade => trade.RealizedPnlMxn > 0).ToList();
        var losses = sells.Where(trade => trade.RealizedPnlMxn < 0).ToList();
        var total = sells.Sum(trade => trade.RealizedPnlMxn!.Value);
        var closed = sells.Count;
        var winRate = closed == 0 ? 0m : (decimal)wins.Count / closed * 100m;
        var avgWin = wins.Count == 0 ? 0m : wins.Average(trade => trade.RealizedPnlMxn!.Value);
        var avgLoss = losses.Count == 0 ? 0m : losses.Average(trade => trade.RealizedPnlMxn!.Value);
        var avgPnl = closed == 0 ? 0m : sells.Average(trade => trade.RealizedPnlMxn!.Value);

        var months = sells
            .GroupBy(trade =>
            {
                var local = trade.CreatedAt.ToLocalTime();
                return (local.Year, local.Month);
            })
            .Select(group => new MonthPnl(group.Key.Year, group.Key.Month, group.Sum(item => item.RealizedPnlMxn!.Value)))
            .OrderBy(item => item.Year).ThenBy(item => item.Month)
            .ToList();
        MonthPnl? best = months.Count == 0 ? null : months.OrderByDescending(item => item.RealizedPnl).First();
        MonthPnl? worst = months.Count == 0 ? null : months.OrderBy(item => item.RealizedPnl).First();

        var (ddPct, ddMoney) = MaxDrawdown(equity);

        return new TradingStatsResult(
            closed,
            wins.Count,
            losses.Count,
            decimal.Round(winRate, 2, MidpointRounding.AwayFromZero),
            decimal.Round(avgWin, 2, MidpointRounding.AwayFromZero),
            decimal.Round(avgLoss, 2, MidpointRounding.AwayFromZero),
            decimal.Round(avgPnl, 2, MidpointRounding.AwayFromZero),
            decimal.Round(total, 2, MidpointRounding.AwayFromZero),
            ddPct,
            ddMoney,
            best,
            worst);
    }

    public static (decimal Percent, decimal Money) MaxDrawdown(IReadOnlyList<EquitySnapshotData> equity)
    {
        if (equity.Count < 2)
            return (0m, 0m);

        decimal peak = equity[0].EquityMxn;
        decimal maxDrop = 0;
        decimal maxPct = 0;
        foreach (var point in equity)
        {
            if (point.EquityMxn > peak)
                peak = point.EquityMxn;
            var drop = peak - point.EquityMxn;
            if (drop <= maxDrop)
                continue;
            maxDrop = drop;
            maxPct = peak == 0 ? 0 : drop / peak * 100m;
        }

        return (
            decimal.Round(maxPct, 2, MidpointRounding.AwayFromZero),
            decimal.Round(maxDrop, 2, MidpointRounding.AwayFromZero));
    }
}
