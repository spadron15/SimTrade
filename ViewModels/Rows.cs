using CommunityToolkit.Mvvm.ComponentModel;
using SimTrade.Models;
using SimTrade.Services;

namespace SimTrade.ViewModels;

public partial class AccountRow : ObservableObject
{
    public long Id { get; }
    public decimal InitialCash { get; }
    public DateTime CreatedAt { get; }

    [ObservableProperty] private string _name;
    [ObservableProperty] private decimal _cash;
    [ObservableProperty] private decimal _buyCommissionPercent;
    [ObservableProperty] private decimal _sellCommissionPercent;
    [ObservableProperty] private string _marketKind;
    [ObservableProperty] private string _icon;
    [ObservableProperty] private string _notes;

    public AccountRow(AccountData data)
    {
        Id = data.Id;
        InitialCash = data.InitialCash;
        CreatedAt = data.CreatedAt;
        _name = data.Name;
        _cash = data.Cash;
        _buyCommissionPercent = data.BuyCommissionPercent;
        _sellCommissionPercent = data.SellCommissionPercent;
        _marketKind = MarketKinds.Normalize(data.MarketKind);
        _icon = AccountIconCache.Normalize(data.Icon);
        _notes = data.Notes ?? "";
    }

    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);

    public string CashText => MxFormat.Money(Cash);
    public string InitialText => MxFormat.Money(InitialCash);
    public string BuyText => MxFormat.Percent(BuyCommissionPercent);
    public string SellText => MxFormat.Percent(SellCommissionPercent);
    public string CreatedText => CreatedAt.ToLocalTime().ToString("dd MMM yyyy", MxFormat.Culture);
    public bool IsCrypto => MarketKinds.IsCrypto(MarketKind);
    public string MarketKindText => MarketKinds.Label(MarketKind);
    public string SummaryText => $"Efectivo {CashText} · {MarketKindText} · creada el {CreatedText}";

    public void Apply(AccountData data)
    {
        Name = data.Name;
        Cash = data.Cash;
        BuyCommissionPercent = data.BuyCommissionPercent;
        SellCommissionPercent = data.SellCommissionPercent;
        MarketKind = MarketKinds.Normalize(data.MarketKind);
        Icon = AccountIconCache.Normalize(data.Icon);
        Notes = data.Notes ?? "";
    }

    public override string ToString() => Name;

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(SummaryText));
    partial void OnCashChanged(decimal value)
    {
        OnPropertyChanged(nameof(CashText));
        OnPropertyChanged(nameof(SummaryText));
    }
    partial void OnBuyCommissionPercentChanged(decimal value) => OnPropertyChanged(nameof(BuyText));
    partial void OnSellCommissionPercentChanged(decimal value) => OnPropertyChanged(nameof(SellText));
    partial void OnMarketKindChanged(string value)
    {
        OnPropertyChanged(nameof(IsCrypto));
        OnPropertyChanged(nameof(MarketKindText));
        OnPropertyChanged(nameof(SummaryText));
    }

    partial void OnNotesChanged(string value) => OnPropertyChanged(nameof(HasNotes));

    public static AccountRow From(AccountData data) => new(data);
}

public partial class PositionRow : ObservableObject
{
    public long Id { get; }
    public string Symbol { get; }
    public DateTime? OpenedAt { get; }

    [ObservableProperty] private string _companyName;
    [ObservableProperty] private string _currency;
    [ObservableProperty] private decimal _quantity;
    [ObservableProperty] private decimal _averageCost;
    [ObservableProperty] private decimal _totalCost;

    public bool HasMarket { get; private set; }
    public bool IsStale { get; private set; }
    public bool InPesos { get; private set; }
    public decimal? LastPrice { get; private set; }
    public decimal? MarketPrice { get; private set; }
    public decimal? MarketValue { get; private set; }
    public decimal? DayChangePercent { get; private set; }
    public decimal? DayContribution { get; private set; }
    public double? Rsi { get; private set; }
    public decimal? Unrealized { get; private set; }
    public decimal? UnrealizedPercent { get; private set; }
    public decimal? Weight { get; private set; }
    public string MarketDetail { get; private set; } = "Consultando precio…";
    public string WeightText { get; private set; } = "—";
    public string RsiText { get; private set; } = "—";
    public string RsiSignal { get; private set; } = "—";
    public string RsiTone { get; private set; } = "Unknown";
    public string DayContributionText { get; private set; } = "—";
    public string HeatTone { get; private set; } = "Flat";
    public int DayRank { get; private set; }
    public decimal? AverageUsd { get; private set; }
    public string AverageUsdText { get; private set; } = "—";

    public PositionRow(PositionData data)
    {
        Id = data.Id;
        Symbol = data.Symbol;
        OpenedAt = data.OpenedAt;
        _companyName = data.CompanyName;
        _currency = data.Currency;
        _quantity = data.Quantity;
        _averageCost = data.AverageCost;
        _totalCost = data.TotalCost;
    }

    public string OpenedText => OpenedAt is null
        ? "—"
        : OpenedAt.Value.ToLocalTime().ToString("dd MMM yyyy  HH:mm", MxFormat.Culture);
    public string QuantityText => MxFormat.Quantity(Quantity);
    public string AverageText => MxFormat.Price(AverageCost);
    public string CostText => MxFormat.Money(TotalCost);

    public void ApplyFx(decimal? usdMxn)
    {
        if (usdMxn is not > 0 || AverageCost <= 0)
        {
            AverageUsd = null;
            AverageUsdText = "—";
        }
        else
        {
            AverageUsd = decimal.Round(AverageCost / usdMxn.Value, 4, MidpointRounding.AwayFromZero);
            AverageUsdText = MxFormat.Plain(AverageUsd.Value) + " USD";
        }

        OnPropertyChanged(nameof(AverageUsd));
        OnPropertyChanged(nameof(AverageUsdText));
    }
    public string LastPriceText => LastPrice is null
        ? "—"
        : InPesos
            ? MxFormat.Price(LastPrice.Value)
            : MxFormat.Plain(LastPrice.Value) + " " + Currency;
    public string PriceMxnText => !HasMarket || MarketPrice is null
        ? "—"
        : InPesos
            ? "—"
            : MxFormat.Price(MarketPrice.Value);
    public string MarketPriceText => MarketPrice is null ? "—" : MxFormat.Price(MarketPrice.Value);
    public string MarketValueText => MarketValue is null ? CostText : MxFormat.Money(MarketValue.Value);
    public string DayChangeText => DayChangePercent is null ? "—" : MxFormat.SignedPercent(DayChangePercent.Value);
    public string UnrealizedText => Unrealized is null ? "—" : MxFormat.SignedMoney(Unrealized.Value);
    public string UnrealizedPercentText => UnrealizedPercent is null ? "—" : MxFormat.SignedPercent(UnrealizedPercent.Value);
    public string Tone => Unrealized is > 0 ? "Up" : Unrealized is < 0 ? "Down" : "Flat";
    public string DayTone => DayChangePercent is > 0 ? "Up" : DayChangePercent is < 0 ? "Down" : "Flat";
    public string RankText => DayRank <= 0 ? "—" : DayRank.ToString(MxFormat.Culture);

    public void ApplyMarket(PesosQuote quote)
    {
        HasMarket = true;
        IsStale = quote.IsStale;
        InPesos = quote.InPesos;
        LastPrice = quote.PriceOriginal;
        MarketPrice = quote.PriceMxn;
        MarketValue = PortfolioMath.RoundMoney(Quantity * quote.PriceMxn);
        Unrealized = MarketValue.Value - TotalCost;
        UnrealizedPercent = TotalCost == 0 ? null : Unrealized.Value / TotalCost * 100m;
        DayChangePercent = DayPercent(quote);
        DayContribution = DayChangePercent is decimal pct
            ? PortfolioMath.RoundMoney(MarketValue.Value * pct / 100m)
            : null;
        DayContributionText = DayContribution is null ? "—" : MxFormat.SignedMoney(DayContribution.Value);
        HeatTone = DayContribution is > 0 ? "Up" : DayContribution is < 0 ? "Down" : "Flat";
        if (!string.IsNullOrWhiteSpace(quote.CompanyName))
            CompanyName = quote.CompanyName;
        Currency = string.IsNullOrWhiteSpace(quote.Currency) ? Currency : quote.Currency;
        var baseDetail = quote.InPesos
            ? "Pesos mexicanos"
            : $"{MxFormat.Plain(quote.PriceOriginal)} {quote.Currency}  ×  {MxFormat.Fx(quote.FxRate ?? 0)}";
        if (quote.IsStale)
        {
            var local = quote.FetchedAtUtc.ToLocalTime();
            MarketDetail = baseDetail + " · desactualizado " + local.ToString("dd MMM HH:mm", MxFormat.Culture);
        }
        else
            MarketDetail = baseDetail;
        NotifyMarket();
    }

    public void SetDayRank(int rank)
    {
        DayRank = rank;
        OnPropertyChanged(nameof(DayRank));
        OnPropertyChanged(nameof(RankText));
    }

    public void SetWeight(decimal? weight)
    {
        Weight = weight;
        WeightText = weight is null ? "—" : weight.Value.ToString("0.0", MxFormat.Culture) + "%";
        OnPropertyChanged(nameof(Weight));
        OnPropertyChanged(nameof(WeightText));
    }

    public void ApplyRsi(double? rsi)
    {
        Rsi = rsi;
        RsiText = rsi is double value
            ? value.ToString("0.00", MxFormat.Culture)
            : "—";
        RsiSignal = RsiCalculator.Signal(rsi);
        RsiTone = RsiCalculator.SignalKind(rsi);
        OnPropertyChanged(nameof(Rsi));
        OnPropertyChanged(nameof(RsiText));
        OnPropertyChanged(nameof(RsiSignal));
        OnPropertyChanged(nameof(RsiTone));
    }

    private static decimal? DayPercent(PesosQuote quote)
    {
        if (quote.PreviousOriginal is not decimal previous || previous == 0)
            return null;
        return (quote.PriceOriginal - previous) / previous * 100m;
    }

    private void NotifyMarket()
    {
        OnPropertyChanged(nameof(HasMarket));
        OnPropertyChanged(nameof(IsStale));
        OnPropertyChanged(nameof(InPesos));
        OnPropertyChanged(nameof(LastPrice));
        OnPropertyChanged(nameof(LastPriceText));
        OnPropertyChanged(nameof(PriceMxnText));
        OnPropertyChanged(nameof(MarketDetail));
        OnPropertyChanged(nameof(MarketPrice));
        OnPropertyChanged(nameof(MarketPriceText));
        OnPropertyChanged(nameof(MarketValue));
        OnPropertyChanged(nameof(MarketValueText));
        OnPropertyChanged(nameof(DayChangePercent));
        OnPropertyChanged(nameof(DayChangeText));
        OnPropertyChanged(nameof(DayTone));
        OnPropertyChanged(nameof(DayContribution));
        OnPropertyChanged(nameof(DayContributionText));
        OnPropertyChanged(nameof(HeatTone));
        OnPropertyChanged(nameof(Unrealized));
        OnPropertyChanged(nameof(UnrealizedText));
        OnPropertyChanged(nameof(UnrealizedPercent));
        OnPropertyChanged(nameof(UnrealizedPercentText));
        OnPropertyChanged(nameof(Tone));
        OnPropertyChanged(nameof(Currency));
        OnPropertyChanged(nameof(AverageText));
        OnPropertyChanged(nameof(AverageUsd));
        OnPropertyChanged(nameof(AverageUsdText));
    }

    public static PositionRow From(PositionData data) => new(data);
}

public sealed class TradeRow
{
    public long TradeId { get; init; }
    public required string When { get; init; }
    public required string Side { get; init; }
    public required string Tone { get; init; }
    public required string Symbol { get; init; }
    public required string Company { get; init; }
    public required string Quantity { get; init; }
    public required string Price { get; init; }
    public required string Reference { get; init; }
    public required string Gross { get; init; }
    public required string Commission { get; init; }
    public required string Realized { get; init; }
    public required string RealizedTone { get; init; }
    public required string CashAfter { get; init; }
    public required string AverageAfter { get; init; }
    public DateTime CreatedAt { get; init; }
    public string SideKey { get; init; } = "";
    public string SymbolKey { get; init; } = "";
    public bool IsCash { get; init; }
    public string Notes { get; set; } = "";
    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);
    public string NotesPreview => string.IsNullOrWhiteSpace(Notes)
        ? "—"
        : Notes.Length <= 48 ? Notes : Notes[..45] + "…";
    public bool CanEditNotes => !IsCash && TradeId > 0;

    public static TradeRow From(TradeData data)
    {
        var buy = data.Side.Equals("BUY", StringComparison.OrdinalIgnoreCase);
        var reference = data.FxRate is null || data.QuoteCurrency.Equals("MXN", StringComparison.OrdinalIgnoreCase)
            ? "Pesos mexicanos"
            : $"{MxFormat.Plain(data.QuotePrice)} {data.QuoteCurrency}  ×  {MxFormat.Fx(data.FxRate.Value)}";

        string realized;
        string realizedTone;
        if (buy || data.RealizedPnlMxn is null)
        {
            realized = "—";
            realizedTone = "Flat";
        }
        else
        {
            realized = MxFormat.SignedMoney(data.RealizedPnlMxn.Value);
            realizedTone = data.RealizedPnlMxn.Value > 0 ? "Up" : data.RealizedPnlMxn.Value < 0 ? "Down" : "Flat";
        }

        return new TradeRow
        {
            TradeId = data.Id,
            When = data.CreatedAt.ToLocalTime().ToString("dd MMM yyyy  HH:mm", MxFormat.Culture),
            Side = buy ? "Compra" : "Venta",
            Tone = buy ? "Buy" : "Sell",
            Symbol = data.Symbol,
            Company = data.CompanyName,
            Quantity = MxFormat.Quantity(data.Quantity),
            Price = MxFormat.Price(data.PriceMxn),
            Reference = reference,
            Gross = MxFormat.Money(data.GrossMxn),
            Commission = MxFormat.Money(data.CommissionMxn),
            Realized = realized,
            RealizedTone = realizedTone,
            CashAfter = MxFormat.Money(data.CashAfter),
            AverageAfter = data.QuantityAfter == 0 ? "Cerrada" : MxFormat.Price(data.AverageCostAfter),
            CreatedAt = data.CreatedAt,
            SideKey = buy ? "BUY" : "SELL",
            SymbolKey = data.Symbol,
            IsCash = false,
            Notes = data.Notes ?? ""
        };
    }

    public static TradeRow FromCash(CashMovementData data)
    {
        var deposit = CashMovementKinds.IsDeposit(data.Kind);
        var note = string.IsNullOrWhiteSpace(data.Note) ? "Movimiento de efectivo" : data.Note.Trim();
        return new TradeRow
        {
            TradeId = 0,
            When = data.CreatedAt.ToLocalTime().ToString("dd MMM yyyy  HH:mm", MxFormat.Culture),
            Side = CashMovementKinds.Label(data.Kind),
            Tone = deposit ? "Buy" : "Sell",
            Symbol = "Efectivo",
            Company = note,
            Quantity = "—",
            Price = "—",
            Reference = deposit ? "Aportación a la cuenta" : "Retiro de la cuenta",
            Gross = MxFormat.Money(data.AmountMxn),
            Commission = "—",
            Realized = "—",
            RealizedTone = "Flat",
            CashAfter = MxFormat.Money(data.CashAfter),
            AverageAfter = "—",
            CreatedAt = data.CreatedAt,
            SideKey = deposit ? "DEPOSIT" : "WITHDRAW",
            SymbolKey = "",
            IsCash = true
        };
    }
}

public sealed class PendingOrderRow
{
    public long Id { get; }
    public string Symbol { get; }
    public string CompanyName { get; }
    public string Currency { get; }
    public string Side { get; }
    public string OrderType { get; }
    public decimal Quantity { get; }
    public decimal ReservedMxn { get; }
    public decimal? LimitPriceMxn { get; }
    public string SideText { get; }
    public string TypeText { get; }
    public string Tone { get; }
    public string QuantityText { get; }
    public string HoldText { get; }
    public string LimitText { get; }
    public string WhenText { get; }
    public string ExpiresText { get; }
    public bool IsBuy { get; }
    public bool IsLimit { get; }

    private PendingOrderRow(PendingOrderData data)
    {
        Id = data.Id;
        Symbol = data.Symbol;
        CompanyName = data.CompanyName;
        Currency = data.Currency;
        Side = data.Side;
        OrderType = OrderTypes.Normalize(data.OrderType);
        Quantity = data.Quantity;
        ReservedMxn = data.ReservedMxn;
        LimitPriceMxn = data.LimitPriceMxn;
        IsBuy = data.IsBuy;
        IsLimit = data.NeedsTrigger;
        SideText = IsBuy ? "Compra" : "Venta";
        TypeText = OrderTypes.Label(OrderType);
        Tone = IsBuy ? "Buy" : "Sell";
        QuantityText = MxFormat.Quantity(data.Quantity);
        HoldText = IsBuy ? "Apartado " + MxFormat.Money(data.ReservedMxn) : "Cantidad apartada";
        LimitText = data.NeedsTrigger && data.LimitPriceMxn is decimal limit
            ? OrderTypes.Label(OrderType) + " " + MxFormat.Price(limit)
            : "Mercado al abrir";
        WhenText = data.CreatedAt.ToLocalTime().ToString("dd MMM yyyy  HH:mm", MxFormat.Culture);
        ExpiresText = data.ExpiresAt is DateTime expires
            ? "Vence " + expires.ToLocalTime().ToString("dd MMM yyyy", MxFormat.Culture)
            : "Hasta la apertura";
    }

    public static PendingOrderRow From(PendingOrderData data) => new(data);
}

public partial class PriceRangeCard : ObservableObject
{
    public PriceRangeCard(string prompt) => Prompt = prompt;

    public string Prompt { get; }

    [ObservableProperty] private string _symbol = "";
    [ObservableProperty] private string _companyName = "";
    [ObservableProperty] private string _changeText = "—";
    [ObservableProperty] private string _changeTone = "Flat";
    [ObservableProperty] private bool _hasSelection;
    [ObservableProperty] private PriceRangeMark _dayRange = PriceRangeMark.Empty;
    [ObservableProperty] private PriceRangeMark _weekRange = PriceRangeMark.Empty;
}

public sealed class PerformanceRow
{
    public long AccountId { get; init; }
    public required string Name { get; init; }
    public string Icon { get; init; } = "";
    public string Notes { get; init; } = "";
    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);
    public bool IsActive { get; init; }
    public required string MarketKind { get; init; }
    public string MarketKindText => MarketKinds.Label(MarketKind);
    public int AssetCount { get; init; }
    public required string AssetCountText { get; init; }
    public required string CashText { get; init; }
    public required string MarketValueText { get; init; }
    public required string EquityText { get; init; }
    public required string ReturnText { get; init; }
    public required string ReturnSinceText { get; init; }
    public required string AnnualizedText { get; init; }
    public bool ShowAnnualized { get; init; }
    public bool ShowReturnHint { get; init; }
    public required string ReturnTone { get; init; }
    public decimal EquityValue { get; init; }
    public decimal InvestedValue { get; init; }
    public decimal ProfitValue { get; init; }
    public decimal CashValue { get; init; }
    public decimal MarketValue { get; init; }
}
