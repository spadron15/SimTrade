namespace SimTrade.Models;

public static class MarketKinds
{
    public const string Stocks = "Stocks";
    public const string Crypto = "Crypto";

    public static string Normalize(string? value) =>
        IsCrypto(value) ? Crypto : Stocks;

    public static bool IsCrypto(string? value) =>
        string.Equals(value, Crypto, StringComparison.OrdinalIgnoreCase);

    public static string Label(string? value) =>
        IsCrypto(value) ? "Crypto" : "Bolsa";
}

public static class OrderTypes
{
    public const string Market = "Market";
    public const string Limit = "Limit";
    public const string StopLoss = "StopLoss";
    public const string TakeProfit = "TakeProfit";

    public static string Normalize(string? value)
    {
        if (IsLimit(value))
            return Limit;
        if (IsStopLoss(value))
            return StopLoss;
        if (IsTakeProfit(value))
            return TakeProfit;
        return Market;
    }

    public static bool IsMarket(string? value) =>
        string.IsNullOrWhiteSpace(value)
        || string.Equals(value, Market, StringComparison.OrdinalIgnoreCase);

    public static bool IsLimit(string? value) =>
        string.Equals(value, Limit, StringComparison.OrdinalIgnoreCase);

    public static bool IsStopLoss(string? value) =>
        string.Equals(value, StopLoss, StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "Stop", StringComparison.OrdinalIgnoreCase);

    public static bool IsTakeProfit(string? value) =>
        string.Equals(value, TakeProfit, StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "TP", StringComparison.OrdinalIgnoreCase);

    public static bool NeedsTriggerPrice(string? value) =>
        IsLimit(value) || IsStopLoss(value) || IsTakeProfit(value);

    public static bool CancelsAtStockClose(string? value) =>
        IsLimit(value);

    public static string Label(string? value) => Normalize(value) switch
    {
        Limit => "Limitada",
        StopLoss => "Stop",
        TakeProfit => "Take profit",
        _ => "Mercado"
    };
}

public static class CashMovementKinds
{
    public const string Deposit = "Deposit";
    public const string Withdraw = "Withdraw";

    public static bool IsDeposit(string? value) =>
        string.Equals(value, Deposit, StringComparison.OrdinalIgnoreCase);

    public static string Label(string? value) =>
        IsDeposit(value) ? "Aportación" : "Retiro";
}

public sealed class AccountData
{
    public long Id { get; init; }
    public required string Name { get; init; }
    public decimal InitialCash { get; init; }
    public decimal Cash { get; init; }
    public decimal BuyCommissionPercent { get; init; }
    public decimal SellCommissionPercent { get; init; }
    public required string MarketKind { get; init; }
    public string Icon { get; init; } = "";
    public string Notes { get; init; } = "";
    public DateTime CreatedAt { get; init; }

    public bool IsCrypto => MarketKinds.IsCrypto(MarketKind);
}

public sealed class PositionData
{
    public long Id { get; init; }
    public required string Symbol { get; init; }
    public required string CompanyName { get; init; }
    public required string Currency { get; init; }
    public decimal Quantity { get; init; }
    public decimal AverageCost { get; init; }
    public decimal TotalCost { get; init; }
    public DateTime? OpenedAt { get; init; }
}

public sealed class TradeData
{
    public long Id { get; init; }
    public required string Symbol { get; init; }
    public required string CompanyName { get; init; }
    public required string Side { get; init; }
    public decimal Quantity { get; init; }
    public decimal PriceMxn { get; init; }
    public decimal QuotePrice { get; init; }
    public required string QuoteCurrency { get; init; }
    public decimal? FxRate { get; init; }
    public decimal GrossMxn { get; init; }
    public decimal CommissionMxn { get; init; }
    public decimal? RealizedPnlMxn { get; init; }
    public decimal CashAfter { get; init; }
    public decimal AverageCostAfter { get; init; }
    public decimal QuantityAfter { get; init; }
    public DateTime CreatedAt { get; init; }
    public string Notes { get; init; } = "";
}

public sealed class CashMovementData
{
    public long Id { get; init; }
    public long AccountId { get; init; }
    public required string Kind { get; init; }
    public decimal AmountMxn { get; init; }
    public decimal CashAfter { get; init; }
    public string Note { get; init; } = "";
    public DateTime CreatedAt { get; init; }
}

public sealed class SymbolHit
{
    public required string Symbol { get; init; }
    public required string Name { get; init; }
    public required string Exchange { get; init; }
    public required string Kind { get; init; }
    public string Label => string.IsNullOrWhiteSpace(Exchange)
        ? $"{Symbol}    {Name}"
        : $"{Symbol}    {Name}    {Exchange}";
}

public sealed record PesosQuote
{
    public required string Symbol { get; init; }
    public required string CompanyName { get; init; }
    public required string Exchange { get; init; }
    public required string Currency { get; init; }
    public required string Instrument { get; init; }
    public decimal PriceOriginal { get; init; }
    public decimal? PreviousOriginal { get; init; }
    public decimal PriceMxn { get; init; }
    public decimal? PreviousMxn { get; init; }
    public decimal? FxRate { get; init; }
    public bool InPesos { get; init; }
    public bool MarketOpen { get; init; }
    public decimal? SessionOpenOriginal { get; init; }
    public decimal? SessionOpenMxn { get; init; }
    public decimal? DayLowOriginal { get; init; }
    public decimal? DayHighOriginal { get; init; }
    public decimal? DayLowMxn { get; init; }
    public decimal? DayHighMxn { get; init; }
    public decimal? FiftyTwoWeekLowOriginal { get; init; }
    public decimal? FiftyTwoWeekHighOriginal { get; init; }
    public decimal? FiftyTwoWeekLowMxn { get; init; }
    public decimal? FiftyTwoWeekHighMxn { get; init; }
    public DateTime FetchedAtUtc { get; init; } = DateTime.UtcNow;
    public bool IsStale { get; init; }
}

public sealed class PriceRangeMark
{
    public static PriceRangeMark Empty { get; } = new();

    public string LowNativeText { get; init; } = "—";
    public string LowMxnText { get; init; } = "—";
    public string HighNativeText { get; init; } = "—";
    public string HighMxnText { get; init; } = "—";
    public string CurrentNativeText { get; init; } = "—";
    public string CurrentMxnText { get; init; } = "—";
    public bool HasRange { get; init; }
    public bool HasMarker { get; init; }
    public double Marker { get; init; } = 0.5;
}

public sealed class PendingOrderData
{
    public long Id { get; init; }
    public long AccountId { get; init; }
    public required string Symbol { get; init; }
    public required string CompanyName { get; init; }
    public required string Currency { get; init; }
    public required string Side { get; init; }
    public required string OrderType { get; init; }
    public decimal Quantity { get; init; }
    public decimal ReservedMxn { get; init; }
    public decimal QuotePrice { get; init; }
    public decimal? LimitPriceMxn { get; init; }
    public decimal? FxRate { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? ExpiresAt { get; init; }

    public bool IsLimit => OrderTypes.IsLimit(OrderType);
    public bool IsStopLoss => OrderTypes.IsStopLoss(OrderType);
    public bool IsTakeProfit => OrderTypes.IsTakeProfit(OrderType);
    public bool NeedsTrigger => OrderTypes.NeedsTriggerPrice(OrderType);
    public bool IsBuy => Side.Equals("BUY", StringComparison.OrdinalIgnoreCase);
}

public sealed class EquitySnapshotData
{
    public long Id { get; init; }
    public long AccountId { get; init; }
    public DateOnly AsOfDate { get; init; }
    public decimal EquityMxn { get; init; }
    public decimal MarketValueMxn { get; init; }
    public decimal CashMxn { get; init; }
    public DateTime CapturedAt { get; init; }
}

public readonly record struct OperationResult(bool Ok, string Message, long Id = 0);
