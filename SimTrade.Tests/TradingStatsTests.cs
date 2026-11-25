using SimTrade.Models;
using SimTrade.Services;

namespace SimTrade.Tests;

public class TradingStatsTests
{
    [Fact]
    public void Compute_ReportsWinRateAndMonths()
    {
        var trades = new List<TradeData>
        {
            Sell(100m, new DateTime(2026, 1, 10)),
            Sell(-40m, new DateTime(2026, 1, 20)),
            Sell(80m, new DateTime(2026, 2, 5))
        };

        var equity = new List<EquitySnapshotData>
        {
            Snap(1, 1000m),
            Snap(2, 1200m),
            Snap(3, 900m),
            Snap(4, 1100m)
        };

        var stats = TradingStats.Compute(trades, equity);
        Assert.Equal(3, stats.ClosedTrades);
        Assert.Equal(2, stats.Wins);
        Assert.Equal(1, stats.Losses);
        Assert.Equal(66.67m, stats.WinRatePercent);
        Assert.Equal(140m, stats.TotalRealized);
        Assert.Equal(300m, stats.MaxDrawdownMoney);
        Assert.Equal(25m, stats.MaxDrawdownPercent);
        Assert.NotNull(stats.BestMonth);
        Assert.Equal(2, stats.BestMonth!.Month);
        Assert.Equal(80m, stats.BestMonth.RealizedPnl);
    }

    [Fact]
    public void Sma_ComputesRollingAverage()
    {
        double[] values = [1, 2, 3, 4, 5];
        var sma = IndicatorMath.Sma(values, 3);
        Assert.Null(sma[0]);
        Assert.Null(sma[1]);
        Assert.Equal(2d, sma[2]);
        Assert.Equal(3d, sma[3]);
        Assert.Equal(4d, sma[4]);
    }

    private static TradeData Sell(decimal pnl, DateTime when) => new()
    {
        Id = 1,
        Symbol = "AAPL",
        CompanyName = "Apple",
        Side = "SELL",
        Quantity = 1,
        PriceMxn = 100,
        QuotePrice = 100,
        QuoteCurrency = "USD",
        GrossMxn = 100,
        CommissionMxn = 0,
        RealizedPnlMxn = pnl,
        CashAfter = 0,
        AverageCostAfter = 0,
        QuantityAfter = 0,
        CreatedAt = DateTime.SpecifyKind(when, DateTimeKind.Utc),
        Notes = ""
    };

    private static EquitySnapshotData Snap(int day, decimal equity) => new()
    {
        Id = day,
        AccountId = 1,
        AsOfDate = new DateOnly(2026, 1, day),
        EquityMxn = equity,
        MarketValueMxn = equity,
        CashMxn = 0,
        CapturedAt = DateTime.UtcNow
    };
}
