namespace SimTrade.Services;

public static class PortfolioMath
{
    public const int StocksQuantityDecimals = 6;
    public const int CryptoQuantityDecimals = 8;

    public static decimal RoundMoney(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    public static decimal Gross(decimal quantity, decimal price) =>
        RoundMoney(quantity * price);

    public static decimal Commission(decimal gross, decimal percent) =>
        RoundMoney(gross * percent / 100m);

    public static int QuantityDecimals(bool crypto) =>
        crypto ? CryptoQuantityDecimals : StocksQuantityDecimals;

    public static decimal RoundQuantity(decimal quantity, bool crypto) =>
        decimal.Round(quantity, QuantityDecimals(crypto), MidpointRounding.AwayFromZero);

    public static decimal MaxAffordableQuantity(decimal freeCash, decimal price, decimal commissionPercent, bool crypto)
    {
        if (freeCash <= 0 || price <= 0)
            return 0;
        var factor = 1m + (commissionPercent / 100m);
        if (factor <= 0)
            return 0;

        var raw = freeCash / (price * factor);
        return TruncateQuantity(raw, crypto);
    }

    /// <summary>
    /// Compra por monto: el presupuesto incluye comisión.
    /// Venta por monto: el importe es bruto (cantidad × precio), sin comisión.
    /// </summary>
    public static decimal QuantityFromAmount(
        decimal amountMxn,
        decimal price,
        bool buy,
        decimal buyCommissionPercent,
        bool crypto)
    {
        if (amountMxn <= 0 || price <= 0)
            return 0;

        if (buy)
            return MaxAffordableQuantity(amountMxn, price, buyCommissionPercent, crypto);

        return TruncateQuantity(amountMxn / price, crypto);
    }

    public static decimal TruncateQuantity(decimal raw, bool crypto)
    {
        if (raw <= 0)
            return 0;

        if (crypto)
        {
            var scale = (decimal)Math.Pow(10, CryptoQuantityDecimals);
            var truncated = decimal.Floor(raw * scale) / scale;
            return truncated > 0 ? truncated : 0;
        }

        var shares = decimal.Floor(raw);
        return shares > 0 ? shares : 0;
    }

    public static decimal InvestedCapital(decimal initialCash, decimal netDeposits) =>
        initialCash + netDeposits;

    public readonly record struct BuyComputation(
        decimal Gross,
        decimal Commission,
        decimal Debit,
        decimal NewQuantity,
        decimal NewAverage,
        decimal NewTotalCost);

    public static BuyComputation ComputeBuy(
        decimal oldQuantity,
        decimal oldTotalCost,
        decimal quantity,
        decimal price,
        decimal commissionPercent)
    {
        var gross = Gross(quantity, price);
        var commission = Commission(gross, commissionPercent);
        var newQuantity = oldQuantity + quantity;
        var newTotal = oldTotalCost + gross;
        var average = newQuantity == 0 ? 0 : newTotal / newQuantity;
        return new(gross, commission, gross + commission, newQuantity, average, newTotal);
    }

    public readonly record struct SellComputation(
        decimal Gross,
        decimal Commission,
        decimal Credit,
        decimal CostRemoved,
        decimal RealizedPnl,
        decimal NewQuantity,
        decimal NewAverage,
        decimal NewTotalCost);

    public static SellComputation ComputeSell(
        decimal oldQuantity,
        decimal oldTotalCost,
        decimal quantity,
        decimal price,
        decimal commissionPercent)
    {
        var gross = Gross(quantity, price);
        var commission = Commission(gross, commissionPercent);
        var costRemoved = quantity == oldQuantity
            ? oldTotalCost
            : RoundMoney(oldTotalCost * quantity / oldQuantity);
        var credit = gross - commission;
        var realized = RoundMoney(credit - costRemoved);
        var newQuantity = oldQuantity - quantity;
        var newTotal = oldTotalCost - costRemoved;
        if (newTotal < 0)
            newTotal = 0;
        var average = newQuantity == 0 ? 0 : newTotal / newQuantity;
        return new(gross, commission, credit, costRemoved, realized, newQuantity, average, newTotal);
    }

    public static bool TryAnnualizedPercent(decimal equity, decimal invested, TimeSpan age, out decimal percent)
    {
        percent = 0;
        var days = age.TotalDays;
        if (days < 1 || invested <= 0 || equity < 0)
            return false;

        var ratio = (double)(equity / invested);
        var growth = Math.Pow(ratio, 365.25 / days) - 1d;
        if (double.IsNaN(growth) || double.IsInfinity(growth))
            return false;

        percent = (decimal)(growth * 100d);
        return true;
    }

    /// <summary>Stop: compra al romper al alza; venta al romper a la baja.</summary>
    public static bool StopTouched(bool buy, decimal marketPrice, decimal stopPrice) =>
        buy ? marketPrice >= stopPrice : marketPrice <= stopPrice;

    /// <summary>Limitada / take-profit: compra al bajar; venta al subir.</summary>
    public static bool LimitTouched(bool buy, decimal marketPrice, decimal limitPrice) =>
        buy ? marketPrice <= limitPrice : marketPrice >= limitPrice;

    public static decimal FillPrice(bool buy, decimal marketPrice, decimal limitPrice) =>
        buy
            ? Math.Min(marketPrice, limitPrice)
            : Math.Max(marketPrice, limitPrice);
}
