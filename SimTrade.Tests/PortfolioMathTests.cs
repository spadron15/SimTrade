using SimTrade.Services;

namespace SimTrade.Tests;

public class PortfolioMathTests
{
    [Fact]
    public void ComputeSell_ReportsRealizedPnl()
    {
        var sell = PortfolioMath.ComputeSell(
            oldQuantity: 10,
            oldTotalCost: 1000m,
            quantity: 4,
            price: 120m,
            commissionPercent: 0m);

        Assert.Equal(480m, sell.Gross);
        Assert.Equal(400m, sell.CostRemoved);
        Assert.Equal(80m, sell.RealizedPnl);
        Assert.Equal(6m, sell.NewQuantity);
        Assert.Equal(600m, sell.NewTotalCost);
    }

    [Fact]
    public void ComputeSell_IncludesCommissionInRealized()
    {
        var sell = PortfolioMath.ComputeSell(10, 1000m, 10, 100m, 1m);
        Assert.Equal(-10m, sell.RealizedPnl);
        Assert.Equal(0m, sell.NewQuantity);
    }

    [Fact]
    public void MaxAffordableQuantity_CryptoAllowsFractional()
    {
        var qty = PortfolioMath.MaxAffordableQuantity(100m, 30000m, 0m, crypto: true);
        Assert.Equal(0.00333333m, qty);
    }

    [Fact]
    public void MaxAffordableQuantity_StocksFloorsToWholeShares()
    {
        var qty = PortfolioMath.MaxAffordableQuantity(250m, 100m, 0m, crypto: false);
        Assert.Equal(2m, qty);
    }

    [Fact]
    public void InvestedCapital_AddsNetDeposits()
    {
        Assert.Equal(1_050_000m, PortfolioMath.InvestedCapital(1_000_000m, 50_000m));
        Assert.Equal(900_000m, PortfolioMath.InvestedCapital(1_000_000m, -100_000m));
    }

    [Theory]
    [InlineData(true, 100, 110, true)]
    [InlineData(true, 100, 90, false)]
    [InlineData(false, 100, 90, true)]
    [InlineData(false, 100, 110, false)]
    public void StopTouched_MatchesDirection(bool buy, decimal stop, decimal market, bool expected)
    {
        Assert.Equal(expected, PortfolioMath.StopTouched(buy, market, stop));
    }

    [Theory]
    [InlineData(true, 100, 90, true)]
    [InlineData(true, 100, 110, false)]
    [InlineData(false, 100, 110, true)]
    [InlineData(false, 100, 90, false)]
    public void LimitTouched_MatchesDirection(bool buy, decimal limit, decimal market, bool expected)
    {
        Assert.Equal(expected, PortfolioMath.LimitTouched(buy, market, limit));
    }

    [Fact]
    public void RoundQuantity_CryptoKeepsEightPlaces()
    {
        var value = PortfolioMath.RoundQuantity(0.123456789m, crypto: true);
        Assert.Equal(0.12345679m, value);
    }

    [Fact]
    public void QuantityFromAmount_BuyIncludesCommission()
    {
        // 1000 presupuesto, precio 100, comisión 1% → factor 1.01 → 9 títulos
        var qty = PortfolioMath.QuantityFromAmount(1000m, 100m, buy: true, buyCommissionPercent: 1m, crypto: false);
        Assert.Equal(9m, qty);
    }

    [Fact]
    public void QuantityFromAmount_SellUsesGross()
    {
        var qty = PortfolioMath.QuantityFromAmount(1000m, 100m, buy: false, buyCommissionPercent: 1m, crypto: false);
        Assert.Equal(10m, qty);
    }
}
