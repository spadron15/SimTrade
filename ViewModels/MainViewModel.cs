using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using SimTrade.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SimTrade.Data;
using SimTrade.Models;
using SimTrade.Services;

namespace SimTrade.ViewModels;

public enum AppPage
{
    Portafolio,
    Operar,
    Rendimiento,
    Analisis,
    Historial,
    Cuentas,
    Acerca
}

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly TradeRepository _repository;
    private readonly YahooMarket _yahoo;
    private readonly CancellationTokenSource _cts = new();
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _lookupCts;
    private int _searchGeneration;
    private bool _suppressAccountChange;
    private bool _deferRefresh;
    private bool _suppressSymbol;
    private bool _suppressSuggest;
    private int _refreshing;
    private CompositionWindow? _composition;
    private IReadOnlyDictionary<string, PesosQuote> _lastQuotes =
        new Dictionary<string, PesosQuote>(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, double?> _lastRsi =
        new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase);
    private bool? _stocksMarketOpen;
    private DateOnly? _lastLocalDate;
    private readonly DispatcherTimer _nasdaqTimer;
    private static readonly TimeZoneInfo MexicoZone = ResolveMexicoZone();

    private static readonly (Color Light, Color Deep)[] SlicePalette =
    [
        (Color.FromRgb(0xC8, 0x84, 0xFC), Color.FromRgb(0x6D, 0x28, 0xD9)),
        (Color.FromRgb(0x67, 0xE8, 0xF9), Color.FromRgb(0x0E, 0x74, 0x90)),
        (Color.FromRgb(0xF9, 0xA8, 0xD4), Color.FromRgb(0xBE, 0x18, 0x5D)),
        (Color.FromRgb(0xFD, 0xE0, 0x68), Color.FromRgb(0xB4, 0x53, 0x09)),
        (Color.FromRgb(0xA5, 0xB4, 0xFC), Color.FromRgb(0x37, 0x30, 0xA3)),
        (Color.FromRgb(0x6E, 0xE7, 0xB7), Color.FromRgb(0x04, 0x78, 0x57)),
        (Color.FromRgb(0xFD, 0xBA, 0x74), Color.FromRgb(0xC2, 0x41, 0x0C)),
        (Color.FromRgb(0xF0, 0xAB, 0xFC), Color.FromRgb(0x86, 0x1E, 0x98))
    ];

    public MainViewModel(TradeRepository repository, YahooMarket yahoo)
    {
        _repository = repository;
        _yahoo = yahoo;
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText = version is null ? "Versión 1.0.0" : "Versión " + version.ToString(3);
        DatabaseHint = "La base de datos es un archivo SQLite en " + AppDatabase.DatabasePath
                       + ". Para usar SimTrade en otra computadora basta con copiar la carpeta de la aplicación; no hay que instalar un servidor.";
        NewInitial = "1000000";
        NewBuy = "0";
        NewSell = "0";
        FxText = "Consultando USD/MXN…";
        UpdatedText = "";
        WindowTitle = "SimTrade";
        NasdaqStatus = NasdaqClock.Describe(DateTimeOffset.Now);
        _nasdaqTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _nasdaqTimer.Tick += (_, _) => NasdaqStatus = NasdaqClock.Describe(DateTimeOffset.Now);
        _nasdaqTimer.Start();
        LoadPortfolioColumns();
        Accounts.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasAccounts));
        PendingOrders.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasPending));
            OnPropertyChanged(nameof(PendingCount));
        };
        if (CollectionViewSource.GetDefaultView(Positions) is ListCollectionView positionsView)
        {
            positionsView.IsLiveSorting = true;
            foreach (var property in new[]
            {
                nameof(PositionRow.Symbol),
                nameof(PositionRow.CompanyName),
                nameof(PositionRow.LastPrice),
                nameof(PositionRow.MarketPrice),
                nameof(PositionRow.DayChangePercent),
                nameof(PositionRow.Rsi),
                nameof(PositionRow.Quantity),
                nameof(PositionRow.AverageCost),
                nameof(PositionRow.MarketValue),
                nameof(PositionRow.Unrealized),
                nameof(PositionRow.Weight)
            })
            {
                positionsView.LiveSortingProperties.Add(property);
            }
        }
    }

    public ObservableCollection<AccountRow> Accounts { get; } = [];
    public ObservableCollection<PositionRow> Positions { get; } = [];
    public ObservableCollection<TradeRow> Trades { get; } = [];
    public ObservableCollection<TradeRow> FilteredTrades { get; } = [];
    public ObservableCollection<SymbolHit> Suggestions { get; } = [];
    public ObservableCollection<PendingOrderRow> PendingOrders { get; } = [];
    public PriceRangeCard PositionRangeCard { get; } = new("Selecciona un activo del portafolio para ver su rango.");
    public PriceRangeCard QuoteRangeCard { get; } = new("Busca un símbolo para ver su rango.");
    public ObservableCollection<PerformanceRow> PerformanceSummaries { get; } = [];
    public ObservableCollection<PositionRow> DayRanking { get; } = [];
    public IReadOnlyList<BenchmarkOption> BenchmarkChoices { get; } = Benchmarks.All;
    public ObservableCollection<string> HistorySideOptions { get; } =
    [
        "Todas",
        "Compras",
        "Ventas",
        "Aportaciones",
        "Retiros"
    ];

    private readonly List<TradeRow> _historyMatches = [];
    public const int HistoryPageSize = 40;

    public string VersionText { get; }
    public string DatabaseHint { get; }
    public string DonationClabe => "072180008110883012";
    public string ContactEmail => "spadron@outlook.com";
    public string DonationName => "Sergio Padrón Arellano";
    public string DonationBank => "Banorte";
    public bool ShowWorkspaceHeader => Page != AppPage.Acerca;

    [ObservableProperty] private AppPage _page = AppPage.Portafolio;
    [ObservableProperty] private bool _isSidebarCollapsed;

    public double SidebarWidth => IsSidebarCollapsed ? 72 : 248;
    public bool IsSidebarExpanded => !IsSidebarCollapsed;
    public string SidebarToggleText => IsSidebarCollapsed ? ">" : "<";
    public string SidebarToggleToolTip => IsSidebarCollapsed ? "Expandir menú" : "Colapsar menú";

    [RelayCommand]
    private void ToggleSidebar()
    {
        IsSidebarCollapsed = !IsSidebarCollapsed;
    }

    partial void OnIsSidebarCollapsedChanged(bool value)
    {
        OnPropertyChanged(nameof(SidebarWidth));
        OnPropertyChanged(nameof(IsSidebarExpanded));
        OnPropertyChanged(nameof(SidebarToggleText));
        OnPropertyChanged(nameof(SidebarToggleToolTip));
    }
    [ObservableProperty] private string _windowTitle = "";
    [ObservableProperty] private AccountRow? _activeAccount;
    [ObservableProperty] private AccountRow? _inspectedAccount;
    [ObservableProperty] private string _statusText = "Crea una cuenta o elige una existente para empezar.";
    [ObservableProperty] private string _statusTone = "Flat";
    [ObservableProperty] private string _nasdaqStatus = "NASDAQ";
    [ObservableProperty] private string _donationStatus = "";
    [ObservableProperty] private string _contactStatus = "";
    [ObservableProperty] private string _cashText = "—";
    [ObservableProperty] private string _marketValueText = "—";
    [ObservableProperty] private string _equityText = "—";
    [ObservableProperty] private string _equityBreakdownText = "";
    [ObservableProperty] private string _returnText = "—";
    [ObservableProperty] private string _returnSinceText = "";
    [ObservableProperty] private string _annualizedText = "";
    [ObservableProperty] private bool _showAnnualized;
    [ObservableProperty] private bool _showReturnHint;
    [ObservableProperty] private string _returnTone = "Flat";
    [ObservableProperty] private string _openPnlText = "—";
    [ObservableProperty] private string _realizedPnlText = "—";
    [ObservableProperty] private string _realizedPnlTone = "Flat";
    [ObservableProperty] private string _investedText = "—";
    [ObservableProperty] private string _fxText = "";
    [ObservableProperty] private decimal? _usdMxnRate;
    [ObservableProperty] private string _updatedText = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private PositionRow? _selectedPosition;
    [ObservableProperty] private bool _isLookingUp;
    [ObservableProperty] private string _cashMoveAmount = "";
    [ObservableProperty] private string _cashMoveNote = "";
    [ObservableProperty] private string _historySymbolFilter = "";
    [ObservableProperty] private string _historySideFilter = "Todas";
    [ObservableProperty] private DateTime? _historyFrom = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private DateTime? _historyTo =
        new(DateTime.Today.Year, DateTime.Today.Month, DateTime.DaysInMonth(DateTime.Today.Year, DateTime.Today.Month));
    [ObservableProperty] private int _historyPage = 1;
    [ObservableProperty] private string _consolidatedEquityText = "—";
    [ObservableProperty] private string _consolidatedCashText = "—";
    [ObservableProperty] private string _consolidatedMarketText = "—";
    [ObservableProperty] private string _consolidatedReturnText = "—";
    [ObservableProperty] private string _consolidatedReturnTone = "Flat";
    [ObservableProperty] private string _consolidatedInvestedText = "—";

    [ObservableProperty] private string _newName = "";
    [ObservableProperty] private bool _newIsCrypto;
    [ObservableProperty] private bool _editIsCrypto;
    [ObservableProperty] private bool _canEditMarketKind;
    [ObservableProperty] private string _newInitial = "";
    [ObservableProperty] private string _newBuy = "";
    [ObservableProperty] private string _newSell = "";
    [ObservableProperty] private string _newIcon = "📈";
    [ObservableProperty] private string _newNotes = "";
    [ObservableProperty] private string _editName = "";
    [ObservableProperty] private string _editBuy = "";
    [ObservableProperty] private string _editSell = "";
    [ObservableProperty] private string _editIcon = "📈";
    [ObservableProperty] private string _editNotes = "";

    public IReadOnlyList<AccountIconCache.AccountIconCategory> AccountIconCategories { get; } =
        AccountIconCache.Categories;

    [ObservableProperty] private string _symbolText = "";
    [ObservableProperty] private string _quantityText = "";
    [ObservableProperty] private string _amountText = "";
    [ObservableProperty] private bool _tradeByAmount;
    [ObservableProperty] private string _priceText = "";
    [ObservableProperty] private bool _isBuy = true;
    private bool _suppressTradeSizing;
    [ObservableProperty] private string _orderKind = OrderTypes.Market;
    [ObservableProperty] private int _pendingValidityDays = 5;
    public IReadOnlyList<int> ValidityDayOptions { get; } = Enumerable.Range(1, 30).ToList();
    [ObservableProperty] private bool _showSuggestions;
    [ObservableProperty] private SymbolHit? _chosenSuggestion;
    [ObservableProperty] private string _marketLine = "Busca una acción o un ETF. Si cotiza en dólares, el precio se muestra en pesos.";
    [ObservableProperty] private string _fxLine = "";
    [ObservableProperty] private string _originLine = "";
    [ObservableProperty] private bool _showFx;
    [ObservableProperty] private string _positionHint = "Sin posición abierta en este símbolo.";
    [ObservableProperty] private string _sessionText = "";
    [ObservableProperty] private bool _showSession;
    [ObservableProperty] private string _reservedText = "";
    [ObservableProperty] private bool _showReserved;

    [ObservableProperty] private bool _hasTradingStats;
    [ObservableProperty] private string _statsClosedTradesText = "—";
    [ObservableProperty] private string _statsWinRateText = "—";
    [ObservableProperty] private string _statsWinsText = "—";
    [ObservableProperty] private string _statsLossesText = "—";
    [ObservableProperty] private string _statsAverageWinText = "—";
    [ObservableProperty] private string _statsAverageLossText = "—";
    [ObservableProperty] private string _statsAveragePnlText = "—";
    [ObservableProperty] private string _statsTotalRealizedText = "—";
    [ObservableProperty] private string _statsTotalRealizedTone = "Flat";
    [ObservableProperty] private string _statsMaxDrawdownText = "—";
    [ObservableProperty] private string _statsBestMonthText = "—";
    [ObservableProperty] private string _statsWorstMonthText = "—";

    [ObservableProperty] private bool _previewVisible;
    [ObservableProperty] private string _previewCaption = "";
    [ObservableProperty] private string _previewGrossText = "";
    [ObservableProperty] private string _previewCommissionCaption = "";
    [ObservableProperty] private string _previewCommissionText = "";
    [ObservableProperty] private string _previewTotalCaption = "";
    [ObservableProperty] private string _previewTotalText = "";
    [ObservableProperty] private string _previewCashAvailableText = "";
    [ObservableProperty] private string _previewCashAfterText = "";
    [ObservableProperty] private string _previewCashAfterTone = "Flat";
    [ObservableProperty] private string _previewAverageText = "";
    [ObservableProperty] private string _previewQuantityText = "";
    [ObservableProperty] private string _previewWarning = "";

    private PesosQuote? _currentQuote;
    public PesosQuote? CurrentQuote
    {
        get => _currentQuote;
        private set
        {
            _currentQuote = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasQuoteChart));
            OnPropertyChanged(nameof(TradeButtonText));
        }
    }

    public bool HasChartSymbol => SelectedPosition is not null;
    public bool HasQuoteChart => CurrentQuote is not null;

    public bool HasAccounts => Accounts.Count > 0;
    public bool HasActive => ActiveAccount is not null;
    public bool HasInspected => InspectedAccount is not null;
    public bool HasPositions => Positions.Count > 0;
    public bool ShowSellPositions => !IsBuy && HasPositions;
    public bool ShowNoSellPositions => !IsBuy && ActiveAccount is not null && !HasPositions;
    public bool HasTrades => Trades.Count > 0;
    public bool HasFilteredHistory => _historyMatches.Count > 0;
    public int HistoryPageCount => Math.Max(1, (int)Math.Ceiling(_historyMatches.Count / (double)HistoryPageSize));
    public string HistoryPageText =>
        _historyMatches.Count == 0
            ? "Sin movimientos en el rango"
            : $"Página {HistoryPage} de {HistoryPageCount} · {_historyMatches.Count} movimientos";
    public bool ShowHistoryPager => _historyMatches.Count > HistoryPageSize;
    public bool CanHistoryPrev => HistoryPage > 1;
    public bool CanHistoryNext => HistoryPage < HistoryPageCount;
    public bool HasPending => PendingOrders.Count > 0;
    public int PendingCount => PendingOrders.Count;
    public bool HasPerformance => PerformanceSummaries.Count > 0;
    public bool HasDayRanking => DayRanking.Count > 0;
    public bool ShowEmptyDayRanking => ActiveAccount is not null && Positions.Count > 0 && DayRanking.Count == 0;
    public bool ShowEmptyTradingStats => ActiveAccount is not null && !HasTradingStats;
    public bool ShowNoAccount => ActiveAccount is null;
    public bool ShowEmptyPortfolio => ActiveAccount is not null && Positions.Count == 0;
    public bool ShowEmptyHistory => ActiveAccount is not null && Trades.Count == 0;
    public bool ShowEmptyFilteredHistory => ActiveAccount is not null && Trades.Count > 0 && _historyMatches.Count == 0;
    public bool ShowEmptyPerformance => !HasAccounts;
    public bool CanActivateInspected => InspectedAccount is not null && InspectedAccount.Id != ActiveAccount?.Id;
    public bool HasPreviewWarning => PreviewWarning.Length > 0;
    public bool IsNotBusy => !IsBusy;
    public bool NeedsTriggerPrice => OrderTypes.NeedsTriggerPrice(OrderKind);
    public bool IsLimitOrder
    {
        get => OrderTypes.IsLimit(OrderKind);
        set
        {
            if (value)
                OrderKind = OrderTypes.Limit;
        }
    }
    public bool IsStopOrder
    {
        get => OrderTypes.IsStopLoss(OrderKind);
        set
        {
            if (value)
                OrderKind = OrderTypes.StopLoss;
        }
    }
    public bool IsTakeProfitOrder
    {
        get => OrderTypes.IsTakeProfit(OrderKind);
        set
        {
            if (value)
                OrderKind = OrderTypes.TakeProfit;
        }
    }
    public string TradeButtonText => NeedsTriggerPrice
        ? IsBuy
            ? "Poner compra " + OrderTypes.Label(OrderKind).ToLowerInvariant()
            : "Poner venta " + OrderTypes.Label(OrderKind).ToLowerInvariant()
        : !CanTradeNow(CurrentQuote)
            ? IsBuy ? "Poner compra en cola" : "Poner venta en cola"
            : IsBuy ? "Registrar compra" : "Registrar venta";
    public bool IsMarketOrder
    {
        get => OrderTypes.IsMarket(OrderKind);
        set
        {
            if (value)
                OrderKind = OrderTypes.Market;
        }
    }
    public string PriceFieldLabel => OrderKind switch
    {
        OrderTypes.Limit => "Precio límite (MXN)",
        OrderTypes.StopLoss => "Precio stop (MXN)",
        OrderTypes.TakeProfit => "Precio take profit (MXN)",
        _ => "Precio de ejecución (MXN)"
    };
    public string PriceFieldHint => OrderKind switch
    {
        OrderTypes.Limit => "Se ejecuta si el mercado toca este precio o uno mejor. En Bolsa se cancela al cierre; en Crypto permanece hasta ejecutarse o cancelarla.",
        OrderTypes.StopLoss => "Se activa al romper este precio en contra (venta al bajar / compra al subir) y se ejecuta a mercado. No se cancela al cierre.",
        OrderTypes.TakeProfit => "Se activa al alcanzar este precio a favor (venta al subir / compra al bajar) y se ejecuta a mercado. No se cancela al cierre.",
        _ => "El costo promedio usa este precio en pesos. Si el activo cotiza en dólares, el precio sugerido ya incluye el tipo de cambio del momento."
    };
    public string QuantityFieldLabel => ActiveAccount?.IsCrypto == true
        ? "Cantidad (hasta 8 decimales)"
        : "Cantidad de acciones";
    public string AmountFieldLabel => IsBuy
        ? "Monto a invertir (MXN)"
        : "Monto a vender (MXN)";
    public string AmountFieldHint => IsBuy
        ? "El monto incluye la comisión de compra. Se calcula cuántos títulos caben a este precio."
        : "El monto es el importe bruto aproximado (cantidad × precio). Se calcula cuántos títulos vender.";
    public bool TradeByQuantity
    {
        get => !TradeByAmount;
        set
        {
            if (value)
                TradeByAmount = false;
        }
    }
    public string SizingHint
    {
        get
        {
            if (!MxFormat.TryParse(PriceText, out var price) || price <= 0)
                return "";

            if (TradeByAmount)
            {
                if (!MxFormat.TryParse(AmountText, out var amount) || amount <= 0)
                    return "";
                if (!MxFormat.TryParse(QuantityText, out var qty) || qty <= 0)
                    return "Con ese monto no alcanza para ningún título a este precio.";
                return "Equivale a " + MxFormat.Quantity(qty)
                       + (ActiveAccount?.IsCrypto == true ? "" : " acciones")
                       + " a " + MxFormat.Price(price) + ".";
            }

            if (!MxFormat.TryParse(QuantityText, out var quantity) || quantity <= 0)
                return "";
            var gross = PortfolioMath.Gross(quantity, price);
            return IsBuy
                ? "Importe bruto aprox. " + MxFormat.Money(gross) + " (sin contar aún la comisión)."
                : "Importe bruto aprox. " + MxFormat.Money(gross) + ".";
        }
    }
    public bool NewIsStocks
    {
        get => !NewIsCrypto;
        set
        {
            if (value)
                NewIsCrypto = false;
        }
    }
    public bool EditIsStocks
    {
        get => !EditIsCrypto;
        set
        {
            if (value)
                EditIsCrypto = false;
        }
    }
    public bool IsSell
    {
        get => !IsBuy;
        set
        {
            if (value)
                IsBuy = false;
        }
    }

    public bool CanExecuteTrade =>
        !IsBusy && PreviewVisible && PreviewWarning.Length == 0 && ActiveAccount is not null;

    private void NotifyTradeCommand()
    {
        OnPropertyChanged(nameof(CanExecuteTrade));
        ExecuteTradeCommand.NotifyCanExecuteChanged();
    }

    public async Task InitializeAsync()
    {
        try
        {
            _deferRefresh = true;
            var saved = _repository.GetSetting("ActiveAccountId");
            long? preferred = long.TryParse(saved, out var id) ? id : null;
            ReloadAccounts(preferred);
            if (ActiveAccount is null && Accounts.Count > 0)
                ActiveAccount = Accounts[0];
            InspectedAccount = ActiveAccount;
            if (ActiveAccount is null)
            {
                Page = AppPage.Cuentas;
                SetStatus("Crea la primera cuenta. El efectivo inicial sugerido es 1,000,000.00 MXN.", false);
            }
            else
            {
                SetStatus($"Cuenta \"{ActiveAccount.Name}\" lista. Efectivo {MxFormat.Money(ActiveAccount.Cash)}.", false);
            }
            _deferRefresh = false;
            LoadPersistedQuotes();
            PurgeExpiredPendingOrders(announce: true);
            await RefreshQuotesAsync();
            _ = RunRefreshLoopAsync(_cts.Token);
            _ = CheckForUpdatesOnStartupAsync();
        }
        catch (Exception ex)
        {
            SetStatus("No se pudieron cargar los datos. " + ex.Message, true);
        }
    }

    [RelayCommand]
    private void ShowAccounts() => Page = AppPage.Cuentas;

    [RelayCommand]
    private void CreateAccount()
    {
        if (!MxFormat.TryParse(NewInitial, out var initial))
        {
            SetStatus("Escribe el efectivo inicial. El valor sugerido es 1000000.", true);
            return;
        }

        if (!MxFormat.TryParse(NewBuy, out var buy) || !MxFormat.TryParse(NewSell, out var sell))
        {
            SetStatus("Escribe la comisión de compra y la de venta. Usa 0 si no quieres cobrar comisión.", true);
            return;
        }

        var kind = NewIsCrypto ? MarketKinds.Crypto : MarketKinds.Stocks;
        var result = _repository.CreateAccount(NewName, initial, buy, sell, kind, NewIcon, NewNotes);
        if (!result.Ok)
        {
            SetStatus(result.Message, true);
            return;
        }

        var createdName = NewName.Trim();
        NewName = "";
        NewIsCrypto = false;
        NewInitial = "1000000";
        NewBuy = "0";
        NewSell = "0";
        NewIcon = "📈";
        NewNotes = "";
        _deferRefresh = true;
        ReloadAccounts(result.Id);
        _deferRefresh = false;
        InspectedAccount = ActiveAccount;
        Page = AppPage.Portafolio;
        SetStatus($"Cuenta \"{createdName}\" ({MarketKinds.Label(kind)}) lista con {MxFormat.Money(initial)}.", false);
        _ = RefreshQuotesAsync();
    }

    [RelayCommand]
    private void SaveInspected()
    {
        if (InspectedAccount is null)
            return;
        if (!MxFormat.TryParse(EditBuy, out var buy) || !MxFormat.TryParse(EditSell, out var sell))
        {
            SetStatus("Escribe las comisiones como porcentaje. Por ejemplo, 0.10.", true);
            return;
        }

        var kind = EditIsCrypto ? MarketKinds.Crypto : MarketKinds.Stocks;
        var result = _repository.UpdateAccount(InspectedAccount.Id, EditName, buy, sell, kind, EditIcon, EditNotes);
        if (!result.Ok)
        {
            SetStatus(result.Message, true);
            return;
        }

        var data = _repository.GetAccount(InspectedAccount.Id);
        if (data is not null)
        {
            InspectedAccount.Apply(data);
            if (ActiveAccount?.Id == InspectedAccount.Id)
                ActiveAccount.Apply(data);
        }
        if (ActiveAccount?.Id == InspectedAccount.Id)
        {
            WindowTitle = "SimTrade — " + InspectedAccount.Name;
            OnPropertyChanged(nameof(TradeButtonText));
            UpdateMarketLine();
            RecalculatePreview();
        }
        EditName = InspectedAccount.Name;
        EditIsCrypto = InspectedAccount.IsCrypto;
        EditBuy = MxFormat.PercentInput(InspectedAccount.BuyCommissionPercent);
        EditSell = MxFormat.PercentInput(InspectedAccount.SellCommissionPercent);
        EditIcon = string.IsNullOrWhiteSpace(InspectedAccount.Icon) ? "📈" : InspectedAccount.Icon;
        EditNotes = InspectedAccount.Notes;
        CanEditMarketKind = _repository.CountPositions(InspectedAccount.Id) == 0;
        RebuildPerformanceSummaries();
        SetStatus("Se guardaron los datos de la cuenta.", false);
    }

    [RelayCommand]
    private void PickNewIcon(string? icon)
    {
        if (!string.IsNullOrWhiteSpace(icon))
            NewIcon = icon;
    }

    [RelayCommand]
    private void PickEditIcon(string? icon)
    {
        if (!string.IsNullOrWhiteSpace(icon))
            EditIcon = icon;
    }

    [RelayCommand]
    private void ActivateInspected()
    {
        if (InspectedAccount is null)
            return;
        ActiveAccount = InspectedAccount;
        Page = AppPage.Portafolio;
        OnPropertyChanged(nameof(CanActivateInspected));
        SetStatus($"Estás operando la cuenta \"{InspectedAccount.Name}\".", false);
    }

    [RelayCommand]
    private void DeleteInspected()
    {
        if (InspectedAccount is null)
            return;

        var answer = MessageBox.Show(
            $"Se eliminará la cuenta \"{InspectedAccount.Name}\" junto con sus posiciones, su historial y sus órdenes en cola.\n\nEsta acción no se puede deshacer.",
            "Eliminar cuenta",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
            return;

        var id = InspectedAccount.Id;
        var name = InspectedAccount.Name;
        var result = _repository.DeleteAccount(id);
        if (!result.Ok)
        {
            SetStatus(result.Message, true);
            return;
        }

        var next = Accounts.FirstOrDefault(account => account.Id != id);
        var wasActive = ActiveAccount?.Id == id;
        var removed = Accounts.First(account => account.Id == id);
        Accounts.Remove(removed);
        InspectedAccount = next;
        if (wasActive)
            ActiveAccount = next;
        if (ActiveAccount is null)
            Page = AppPage.Cuentas;
        SetStatus($"Se eliminó la cuenta \"{name}\".", false);
    }

    [RelayCommand]
    private async Task LookupSymbolAsync()
    {
        if (string.IsNullOrWhiteSpace(SymbolText))
        {
            SetStatus("Escribe el símbolo. Por ejemplo, WALMEX o AAPL.", true);
            return;
        }

        await LoadQuoteAsync(SymbolText);
    }

    [RelayCommand]
    private void UseMarketPrice()
    {
        if (CurrentQuote is null)
        {
            SetStatus("Primero busca el símbolo para traer el precio de mercado.", true);
            return;
        }

        PriceText = CurrentQuote.PriceMxn.ToString("0.####", MxFormat.Culture);
        RecalculatePreview();
    }

    [RelayCommand]
    private void UseDayLowPrice()
    {
        if (CurrentQuote is null)
        {
            SetStatus("Primero busca el símbolo para traer los precios del día.", true);
            return;
        }

        if (CurrentQuote.DayLowMxn is not decimal low || low <= 0)
        {
            SetStatus("Yahoo Finance no devolvió el precio mínimo del día para este símbolo.", true);
            return;
        }

        OrderKind = OrderTypes.Limit;
        PriceText = low.ToString("0.####", MxFormat.Culture);
        SetStatus(IsBuy
            ? "Orden limitada: se ejecutará si el precio baja a " + MxFormat.Price(low) + " o menos."
            : "Orden limitada: se ejecutará si el precio sube a " + MxFormat.Price(low) + " o más.", false);
        RecalculatePreview();
    }

    [RelayCommand]
    private void UseDayHighPrice()
    {
        if (CurrentQuote is null)
        {
            SetStatus("Primero busca el símbolo para traer los precios del día.", true);
            return;
        }

        if (CurrentQuote.DayHighMxn is not decimal high || high <= 0)
        {
            SetStatus("Yahoo Finance no devolvió el precio máximo del día para este símbolo.", true);
            return;
        }

        OrderKind = OrderTypes.Limit;
        PriceText = high.ToString("0.####", MxFormat.Culture);
        SetStatus(IsBuy
            ? "Orden limitada: se ejecutará si el precio baja a " + MxFormat.Price(high) + " o menos."
            : "Orden limitada: se ejecutará si el precio sube a " + MxFormat.Price(high) + " o más.", false);
        RecalculatePreview();
    }

    [RelayCommand]
    private void UseAllShares()
    {
        var position = FindPosition(SymbolText);
        if (position is null)
        {
            SetStatus("Esta cuenta no tiene acciones de ese símbolo.", true);
            return;
        }

        var free = position.Quantity - ReservedSellQty(position.Symbol);
        if (free <= 0)
        {
            SetStatus("Esas acciones ya están apartadas en la cola.", true);
            return;
        }

        IsBuy = false;
        TradeByAmount = false;
        QuantityText = MxFormat.Quantity(free);
        SyncTradeSizingFromMode();
    }

    [RelayCommand]
    private void CancelPending(PendingOrderRow? order)
    {
        if (order is null || ActiveAccount is null || IsBusy)
            return;

        var result = _repository.CancelPending(ActiveAccount.Id, order.Id);
        SetStatus(result.Message, !result.Ok);
        if (!result.Ok)
            return;

        RefreshActiveSnapshot();
        UpdateReservedBanner();
        RecalculatePreview();
        OnPropertyChanged(nameof(HasPending));
        OnPropertyChanged(nameof(PendingCount));
    }

    [RelayCommand]
    private async Task PrepareBuyAsync(PositionRow? row)
    {
        if (row is null)
            return;
        Page = AppPage.Operar;
        IsBuy = true;
        QuantityText = "";
        await LoadQuoteAsync(row.Symbol);
    }

    [RelayCommand]
    private async Task PrepareSellAsync(PositionRow? row)
    {
        if (row is null)
            return;
        Page = AppPage.Operar;
        IsBuy = false;
        TradeByAmount = false;
        var free = row.Quantity - ReservedSellQty(row.Symbol);
        QuantityText = free > 0 ? MxFormat.Quantity(free) : MxFormat.Quantity(row.Quantity);
        await LoadQuoteAsync(row.Symbol);
        SetStatus("Posición cargada: " + row.Symbol + " · " + QuantityText + " disponibles para vender.", false);
    }

    [RelayCommand(CanExecute = nameof(CanExecuteTrade))]
    private async Task ExecuteTradeAsync()
    {
        if (!CanExecuteTrade || ActiveAccount is null)
            return;

        if (!MxFormat.TryParse(QuantityText, out var quantity) || quantity <= 0)
        {
            SetStatus("Escribe una cantidad mayor que cero.", true);
            return;
        }

        var crypto = ActiveAccount.IsCrypto;
        quantity = PortfolioMath.RoundQuantity(quantity, crypto);
        if (quantity <= 0)
        {
            SetStatus("La cantidad es demasiado pequeña.", true);
            return;
        }

        var position = FindPosition(SymbolText);
        var typed = YahooMarket.Normalize(position?.Symbol ?? SymbolText);
        if (typed is null)
        {
            SetStatus("Escribe el símbolo. Por ejemplo, WALMEX o AAPL.", true);
            return;
        }

        IsBusy = true;
        var completed = false;
        try
        {
            var quote = await _yahoo.PriceInPesosAsync(typed, _cts.Token);
            if (quote is null)
            {
                SetStatus("Yahoo Finance no devolvió precio. No se puede asignar la orden.", true);
                return;
            }

            if (!InstrumentAllowed(quote, out var instrumentError))
            {
                SetStatus(instrumentError, true);
                return;
            }

            position = FindPosition(quote.Symbol);
            if (position is not null && !position.Currency.Equals(quote.Currency, StringComparison.OrdinalIgnoreCase))
            {
                SetStatus("Ese símbolo ya está registrado en la cuenta con otra moneda.", true);
                return;
            }

            CurrentQuote = quote;
            _suppressSymbol = true;
            SymbolText = quote.Symbol;
            _suppressSymbol = false;
            if (!NeedsTriggerPrice)
                PriceText = quote.PriceMxn.ToString("0.####", MxFormat.Culture);
            UpdateMarketLine();
            UpdatePositionHint();
            RecalculatePreview();
            if (PreviewWarning.Length > 0)
            {
                SetStatus(PreviewWarning, true);
                return;
            }

            var symbol = position?.Symbol ?? quote.Symbol;
            var company = string.IsNullOrWhiteSpace(quote.CompanyName) ? position?.CompanyName ?? quote.Symbol : quote.CompanyName;
            var currency = position?.Currency ?? quote.Currency;
            var marketPrice = decimal.Round(quote.PriceMxn, 4, MidpointRounding.AwayFromZero);
            var quotePrice = quote.PriceOriginal;
            decimal? fx = quote.InPesos ? null : quote.FxRate;
            var accountId = ActiveAccount.Id;
            var buy = IsBuy;
            var kind = OrderTypes.Normalize(OrderKind);

            if (NeedsTriggerPrice)
            {
                if (!MxFormat.TryParse(PriceText, out var limitPrice) || limitPrice <= 0)
                {
                    SetStatus("Escribe un precio de activación mayor que cero.", true);
                    return;
                }

                limitPrice = decimal.Round(limitPrice, 4, MidpointRounding.AwayFromZero);
                var touchesNow = OrderTypes.IsStopLoss(kind)
                    ? PortfolioMath.StopTouched(buy, marketPrice, limitPrice)
                    : PortfolioMath.LimitTouched(buy, marketPrice, limitPrice);
                if (CanTradeNow(quote) && touchesNow)
                {
                    var fill = OrderTypes.IsStopLoss(kind)
                        ? marketPrice
                        : PortfolioMath.FillPrice(buy, marketPrice, limitPrice);
                    var immediate = await Task.Run(() => buy
                        ? _repository.Buy(accountId, symbol, company, currency, quantity, fill, quotePrice, fx)
                        : _repository.Sell(accountId, symbol, company, currency, quantity, fill, quotePrice, fx));
                    SetStatus(immediate.Ok
                        ? immediate.Message + " Precio asignado " + MxFormat.Price(fill) + "."
                        : immediate.Message, !immediate.Ok);
                    if (!immediate.Ok)
                        return;
                    NotifyOrderFilled(buy, symbol, quantity, fill, OrderTypes.Label(kind));
                    completed = true;
                    QuantityText = "";
                    RefreshActiveSnapshot();
                    if (!buy && FindPosition(symbol) is null)
                        IsBuy = true;
                    await RefreshQuotesAsync();
                    return;
                }

                var queuedTrigger = await Task.Run(() => _repository.Enqueue(
                    accountId, symbol, company, currency, buy, quantity, marketPrice, quotePrice, fx,
                    kind, limitPrice, PendingValidityDays));
                SetStatus(queuedTrigger.Message, !queuedTrigger.Ok);
                if (!queuedTrigger.Ok)
                    return;
                completed = true;
                QuantityText = "";
                RefreshActiveSnapshot();
                return;
            }

            if (!CanTradeNow(quote))
            {
                var queued = await Task.Run(() => _repository.Enqueue(
                    accountId, symbol, company, currency, buy, quantity, marketPrice, quotePrice, fx));
                SetStatus(queued.Message, !queued.Ok);
                if (!queued.Ok)
                    return;
                completed = true;
                QuantityText = "";
                RefreshActiveSnapshot();
                return;
            }

            var result = await Task.Run(() => buy
                ? _repository.Buy(accountId, symbol, company, currency, quantity, marketPrice, quotePrice, fx)
                : _repository.Sell(accountId, symbol, company, currency, quantity, marketPrice, quotePrice, fx));
            SetStatus(result.Ok ? result.Message + " Precio asignado " + MxFormat.Price(marketPrice) + "." : result.Message, !result.Ok);
            if (!result.Ok)
                return;

            NotifyOrderFilled(buy, symbol, quantity, marketPrice, "Mercado");
            completed = true;
            QuantityText = "";
            RefreshActiveSnapshot();
            if (!buy && FindPosition(symbol) is null)
                IsBuy = true;
            await RefreshQuotesAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            SetStatus("No se pudo registrar la operación. " + ex.Message, true);
        }
        finally
        {
            IsBusy = false;
            if (!completed)
                RecalculatePreview();
            else
                NotifyTradeCommand();
        }
    }

    public async Task RefreshQuotesAsync()
    {
        if (Interlocked.Exchange(ref _refreshing, 1) == 1)
            return;

        try
        {
            await OnUiAsync(() => PurgeExpiredPendingOrders(announce: false));
            var symbols = CollectQuoteSymbols();
            var token = _cts.Token;
            _yahoo.InvalidateFxCache();
            var fxTask = _yahoo.GetUsdMxnAsync(token);
            var quotesTask = _yahoo.PriceManyAsync(symbols, token);
            var rsiTask = _yahoo.GetDailyRsiManyAsync(symbols, token);
            await Task.WhenAll(fxTask, quotesTask, rsiTask).ConfigureAwait(false);

            var fx = fxTask.Result;
            var quotes = quotesTask.Result;
            var rsiMap = rsiTask.Result;
            await OnUiAsync(() => ApplyQuoteRefresh(symbols, fx, quotes, rsiMap));
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            await OnUiAsync(() =>
            {
                UpdatedText = "Sin conexión con Yahoo Finance";
                if (string.IsNullOrWhiteSpace(FxText) || FxText.StartsWith("Consultando", StringComparison.Ordinal))
                    FxText = "USD/MXN no disponible";
                RebuildPerformanceSummaries();
            });
        }
        finally
        {
            Interlocked.Exchange(ref _refreshing, 0);
        }
    }

    private void ApplyQuoteRefresh(
        IReadOnlyList<string> symbols,
        decimal? fx,
        IReadOnlyList<PesosQuote> quotes,
        IReadOnlyDictionary<string, double?> rsiMap)
    {
        UsdMxnRate = fx is decimal rate && rate > 0 ? rate : null;
        FxText = UsdMxnRate is decimal liveFx
            ? "USD/MXN  " + MxFormat.Fx(liveFx)
            : "USD/MXN no disponible";

        var map = new Dictionary<string, PesosQuote>(_lastQuotes, StringComparer.OrdinalIgnoreCase);
        var freshSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var quote in quotes)
        {
            map[quote.Symbol] = quote;
            freshSymbols.Add(quote.Symbol);
            try
            {
                _repository.UpsertQuoteCache(quote);
            }
            catch
            {
                // La caché es auxiliar; no bloquear el refresco.
            }
        }

        // Símbolos sin respuesta de Yahoo: conservar última cotización marcada como desactualizada.
        foreach (var symbol in symbols)
        {
            if (freshSymbols.Contains(symbol))
                continue;
            if (!map.TryGetValue(symbol, out var cached))
                continue;
            if (cached.IsStale)
                continue;
            map[symbol] = cached with { IsStale = true };
        }

        _lastQuotes = map;
        var rsiCache = new Dictionary<string, double?>(_lastRsi, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in rsiMap)
            rsiCache[pair.Key] = pair.Value;
        _lastRsi = rsiCache;
        var settled = SettlePending(map);
        var matched = MatchLimitOrders(map);
        var triggered = MatchStopAndTakeProfit(map);
        var notices = new List<(string Text, bool Failed)>();
        if (settled is not null)
            notices.Add(settled.Value);
        if (matched is not null)
            notices.Add(matched.Value);
        if (triggered is not null)
            notices.Add(triggered.Value);
        if (notices.Count > 0)
            SetStatus(string.Join(" ", notices.Select(item => item.Text)), notices.Any(item => item.Failed));

        foreach (var row in Positions)
        {
            if (map.TryGetValue(row.Symbol, out var quote))
                row.ApplyMarket(quote);
            if (rsiMap.TryGetValue(row.Symbol, out var rsi))
                row.ApplyRsi(rsi);
            row.ApplyFx(UsdMxnRate);
        }

        if (CurrentQuote is not null && map.TryGetValue(CurrentQuote.Symbol, out var live))
        {
            CurrentQuote = live;
            UpdateMarketLine();
            SyncRangeCard(QuoteRangeCard, live.Symbol, live.CompanyName, live);
        }

        if (SelectedPosition is not null && map.TryGetValue(SelectedPosition.Symbol, out var selectedQuote))
            SyncRangeCard(PositionRangeCard, selectedQuote.Symbol, selectedQuote.CompanyName, selectedQuote);

        var staleCount = symbols.Count(symbol => map.TryGetValue(symbol, out var q) && q.IsStale);
        var missing = symbols.Count(symbol => !map.ContainsKey(symbol));
        if (staleCount > 0 || missing > 0)
        {
            UpdatedText = staleCount > 0
                ? "Precios en caché (desactualizados) · " + DateTime.Now.ToString("HH:mm:ss")
                : "Algunos precios no se actualizaron · " + DateTime.Now.ToString("HH:mm:ss");
        }
        else
            UpdatedText = "Precios al " + DateTime.Now.ToString("HH:mm:ss");
        RefreshHeader();
        RebuildDayRanking();
        RebuildTradingStats();
        RecalculatePreview();
        CaptureDailyEquitySnapshots(map);
    }

    private void LoadPersistedQuotes()
    {
        try
        {
            var cached = _repository.LoadQuoteCache();
            if (cached.Count == 0)
                return;
            var map = new Dictionary<string, PesosQuote>(_lastQuotes, StringComparer.OrdinalIgnoreCase);
            foreach (var pair in cached)
                map[pair.Key] = pair.Value;
            _lastQuotes = map;
            ReapplyCachedMarket();
            RebuildDayRanking();
        }
        catch
        {
        }
    }

    private List<string> CollectQuoteSymbols()
    {
        var symbols = new List<string>();
        foreach (var account in Accounts)
        {
            foreach (var position in _repository.ListPositions(account.Id))
            {
                if (!symbols.Contains(position.Symbol, StringComparer.OrdinalIgnoreCase))
                    symbols.Add(position.Symbol);
            }
        }

        foreach (var order in _repository.ListAllPending())
        {
            if (!symbols.Contains(order.Symbol, StringComparer.OrdinalIgnoreCase))
                symbols.Add(order.Symbol);
        }

        if (CurrentQuote is not null && !symbols.Contains(CurrentQuote.Symbol, StringComparer.OrdinalIgnoreCase))
            symbols.Add(CurrentQuote.Symbol);

        return symbols;
    }

    private static Task OnUiAsync(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(action).Task;
    }

    public void Dispose()
    {
        _nasdaqTimer.Stop();
        _cts.Cancel();
        _searchCts?.Cancel();
        _lookupCts?.Cancel();
        _composition?.Close();
        _yahoo.Dispose();
    }

    partial void OnActiveAccountChanged(AccountRow? value)
    {
        OnPropertyChanged(nameof(HasActive));
        OnPropertyChanged(nameof(ShowNoAccount));
        OnPropertyChanged(nameof(CanActivateInspected));
        NotifyTradeCommand();
        WindowTitle = value is null ? "SimTrade" : "SimTrade — " + value.Name;
        if (_suppressAccountChange)
            return;

        if (value is not null)
            _repository.SetSetting("ActiveAccountId", value.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));

        ReloadPositionsAndTrades();
        OnPropertyChanged(nameof(TradeButtonText));
        UpdateMarketLine();
        if (!_deferRefresh)
            _ = RefreshQuotesAsync();
    }

    partial void OnInspectedAccountChanged(AccountRow? value)
    {
        OnPropertyChanged(nameof(HasInspected));
        OnPropertyChanged(nameof(CanActivateInspected));
        if (value is null)
        {
            CanEditMarketKind = false;
            return;
        }
        EditName = value.Name;
        EditIsCrypto = value.IsCrypto;
        EditBuy = MxFormat.PercentInput(value.BuyCommissionPercent);
        EditSell = MxFormat.PercentInput(value.SellCommissionPercent);
        EditIcon = string.IsNullOrWhiteSpace(value.Icon) ? "📈" : value.Icon;
        EditNotes = value.Notes;
        CanEditMarketKind = _repository.CountPositions(value.Id) == 0;
    }

    partial void OnNewIsCryptoChanged(bool value) => OnPropertyChanged(nameof(NewIsStocks));

    partial void OnEditIsCryptoChanged(bool value) => OnPropertyChanged(nameof(EditIsStocks));

    partial void OnPageChanged(AppPage value)
    {
        OnPropertyChanged(nameof(ShowWorkspaceHeader));
        DonationStatus = "";
        ContactStatus = "";
        if (value == AppPage.Rendimiento)
            _ = RefreshQuotesAsync();
    }

    [RelayCommand]
    private void CopyClabe()
    {
        try
        {
            Clipboard.SetText(DonationClabe);
            ContactStatus = "";
            DonationStatus = "CLABE copiada al portapapeles.";
        }
        catch (Exception)
        {
            DonationStatus = "No se pudo copiar la CLABE.";
        }
    }

    [RelayCommand]
    private void CopyContact()
    {
        try
        {
            Clipboard.SetText(ContactEmail);
            DonationStatus = "";
            ContactStatus = "Correo copiado al portapapeles.";
        }
        catch (Exception)
        {
            ContactStatus = "No se pudo copiar el correo.";
        }
    }

    partial void OnIsBuyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsSell));
        OnPropertyChanged(nameof(ShowSellPositions));
        OnPropertyChanged(nameof(ShowNoSellPositions));
        OnPropertyChanged(nameof(TradeButtonText));
        OnPropertyChanged(nameof(AmountFieldLabel));
        OnPropertyChanged(nameof(AmountFieldHint));
        SyncTradeSizingFromMode();
        RecalculatePreview();
    }

    partial void OnTradeByAmountChanged(bool value)
    {
        OnPropertyChanged(nameof(TradeByQuantity));
        OnPropertyChanged(nameof(AmountFieldLabel));
        OnPropertyChanged(nameof(AmountFieldHint));
        SyncTradeSizingFromMode();
        RecalculatePreview();
        OnPropertyChanged(nameof(SizingHint));
    }

    partial void OnOrderKindChanged(string value)
    {
        OnPropertyChanged(nameof(IsMarketOrder));
        OnPropertyChanged(nameof(IsLimitOrder));
        OnPropertyChanged(nameof(IsStopOrder));
        OnPropertyChanged(nameof(IsTakeProfitOrder));
        OnPropertyChanged(nameof(NeedsTriggerPrice));
        OnPropertyChanged(nameof(TradeButtonText));
        OnPropertyChanged(nameof(PriceFieldLabel));
        OnPropertyChanged(nameof(PriceFieldHint));
        UpdateMarketLine();
        RecalculatePreview();
    }

    partial void OnPendingValidityDaysChanged(int value)
    {
        if (value < 1 || value > 30)
            PendingValidityDays = Math.Clamp(value, 1, 30);
    }

    private void PurgeExpiredPendingOrders(bool announce)
    {
        var cancelled = _repository.CancelExpiredPendingOrders();
        if (cancelled <= 0)
            return;

        if (ActiveAccount is not null)
            RefreshActiveSnapshot();
        else
            ReloadAccounts(null);

        UpdateReservedBanner();
        if (announce)
        {
            SetStatus(cancelled == 1
                ? "Se canceló 1 orden vencida y se liberó lo apartado."
                : "Se cancelaron " + cancelled.ToString(MxFormat.Culture) + " órdenes vencidas y se liberó lo apartado.",
                false);
        }
    }

    private string PreviewSideCaption(bool buy)
    {
        if (NeedsTriggerPrice)
            return (buy ? "Compra " : "Venta ") + OrderTypes.Label(OrderKind).ToLowerInvariant();
        if (!CanTradeNow(CurrentQuote))
            return buy ? "Compra en cola" : "Venta en cola";
        return buy ? "Compra" : "Venta";
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotBusy));
        NotifyTradeCommand();
    }

    partial void OnSymbolTextChanged(string value)
    {
        UpdatePositionHint();
        RecalculatePreview();
        if (_suppressSymbol)
            return;
        _ = SearchAsync(value);
    }

    partial void OnQuantityTextChanged(string value)
    {
        if (!_suppressTradeSizing && !TradeByAmount)
            UpdateAmountFromQuantity();
        RecalculatePreview();
        OnPropertyChanged(nameof(SizingHint));
    }

    partial void OnAmountTextChanged(string value)
    {
        if (!_suppressTradeSizing && TradeByAmount)
            UpdateQuantityFromAmount();
        RecalculatePreview();
        OnPropertyChanged(nameof(SizingHint));
    }

    partial void OnPriceTextChanged(string value)
    {
        SyncTradeSizingFromMode();
        RecalculatePreview();
        OnPropertyChanged(nameof(SizingHint));
    }

    private void SyncTradeSizingFromMode()
    {
        if (TradeByAmount)
            UpdateQuantityFromAmount();
        else
            UpdateAmountFromQuantity();
    }

    private void UpdateQuantityFromAmount()
    {
        if (ActiveAccount is null
            || !MxFormat.TryParse(AmountText, out var amount) || amount <= 0
            || !MxFormat.TryParse(PriceText, out var price) || price <= 0)
        {
            SetQuantitySilent("");
            return;
        }

        var qty = PortfolioMath.QuantityFromAmount(
            amount,
            price,
            IsBuy,
            ActiveAccount.BuyCommissionPercent,
            ActiveAccount.IsCrypto);
        SetQuantitySilent(qty > 0 ? MxFormat.Quantity(qty) : "");
    }

    private void UpdateAmountFromQuantity()
    {
        if (!MxFormat.TryParse(QuantityText, out var quantity) || quantity <= 0
            || !MxFormat.TryParse(PriceText, out var price) || price <= 0)
        {
            SetAmountSilent("");
            return;
        }

        SetAmountSilent(PortfolioMath.Gross(quantity, price).ToString("0.##", MxFormat.Culture));
    }

    private void SetQuantitySilent(string value)
    {
        _suppressTradeSizing = true;
        QuantityText = value;
        _suppressTradeSizing = false;
    }

    private void SetAmountSilent(string value)
    {
        _suppressTradeSizing = true;
        AmountText = value;
        _suppressTradeSizing = false;
    }

    partial void OnPreviewWarningChanged(string value)
    {
        OnPropertyChanged(nameof(HasPreviewWarning));
        NotifyTradeCommand();
    }

    partial void OnPreviewVisibleChanged(bool value) => NotifyTradeCommand();

    partial void OnChosenSuggestionChanged(SymbolHit? value)
    {
        if (_suppressSuggest || value is null)
            return;
        _ = LoadQuoteAsync(value.Symbol);
    }

    private async Task SearchAsync(string text)
    {
        _searchCts?.Cancel();
        var cts = new CancellationTokenSource();
        _searchCts = cts;
        var generation = ++_searchGeneration;
        var token = cts.Token;
        try
        {
            await Task.Delay(280, token);
            if (text.Trim().Length == 0)
            {
                ClearSuggestions();
                return;
            }

            var hits = await _yahoo.SearchAsync(text, token);
            if (generation != _searchGeneration || token.IsCancellationRequested)
                return;

            var resolved = YahooMarket.Normalize(SymbolText);
            if (CurrentQuote is not null && resolved is not null
                && CurrentQuote.Symbol.Equals(resolved, StringComparison.OrdinalIgnoreCase))
            {
                ClearSuggestions();
                return;
            }

            Suggestions.Clear();
            foreach (var hit in hits)
                Suggestions.Add(hit);
            ShowSuggestions = Suggestions.Count > 0;
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            ClearSuggestions();
        }
    }

    private async Task LoadQuoteAsync(string symbol)
    {
        var normalized = YahooMarket.Normalize(symbol);
        if (normalized is null)
        {
            SetStatus("El símbolo no es válido.", true);
            return;
        }

        if (normalized.Contains('=') || normalized.StartsWith('^'))
        {
            SetStatus("Ese símbolo no es una acción ni un ETF.", true);
            return;
        }

        _searchGeneration++;
        _searchCts?.Cancel();
        ClearSuggestions();
        _lookupCts?.Cancel();
        _lookupCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        var token = _lookupCts.Token;
        IsLookingUp = true;
        try
        {
            var quote = await _yahoo.PriceInPesosAsync(normalized, token);
            if (token.IsCancellationRequested)
                return;
            if (quote is null)
            {
                CurrentQuote = null;
                UpdateMarketLine();
                SetStatus("Yahoo Finance no devolvió precio para " + normalized + ".", true);
                return;
            }

            var kind = quote.Instrument.ToUpperInvariant();
            if (kind is "CURRENCY" or "INDEX" or "FUTURE" or "OPTION")
            {
                CurrentQuote = null;
                UpdateMarketLine();
                SetStatus("SimTrade opera acciones y ETFs. Ese símbolo no se puede comprar aquí.", true);
                return;
            }

            CurrentQuote = quote;
            _suppressSymbol = true;
            SymbolText = quote.Symbol;
            _suppressSymbol = false;
            ClearSuggestions();
            PriceText = quote.PriceMxn.ToString("0.####", MxFormat.Culture);
            UpdateMarketLine();
            UpdatePositionHint();
            RecalculatePreview();

            var where = quote.InPesos
                ? "cotiza en pesos mexicanos, así que el precio se toma tal cual."
                : $"cotiza en {quote.Currency} y se convirtió a pesos con el tipo de cambio {MxFormat.Fx(quote.FxRate ?? 0)}.";
            SetStatus(quote.Symbol + " " + where, false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            SetStatus("No se pudo consultar Yahoo Finance. " + ex.Message, true);
        }
        finally
        {
            IsLookingUp = false;
        }
    }

    private async Task RunRefreshLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                await RefreshQuotesAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ReloadAccounts(long? selectId)
    {
        var selected = selectId ?? ActiveAccount?.Id;
        _suppressAccountChange = true;
        Accounts.Clear();
        foreach (var account in _repository.ListAccounts())
            Accounts.Add(AccountRow.From(account));
        _suppressAccountChange = false;
        OnPropertyChanged(nameof(HasAccounts));
        OnPropertyChanged(nameof(ShowEmptyPerformance));
        ActiveAccount = selected is long id ? Accounts.FirstOrDefault(account => account.Id == id) : null;
        RebuildPerformanceSummaries();
    }

    private void RefreshActiveSnapshot()
    {
        if (ActiveAccount is null)
            return;
        var data = _repository.GetAccount(ActiveAccount.Id);
        if (data is not null)
            ActiveAccount.Apply(data);
        ReloadPositionsAndTrades();
    }

    private void ReloadPositionsAndTrades()
    {
        var keepId = SelectedPosition?.Id;
        var keepSymbol = SelectedPosition?.Symbol;

        Positions.Clear();
        Trades.Clear();
        PendingOrders.Clear();
        if (ActiveAccount is not null)
        {
            foreach (var position in _repository.ListPositions(ActiveAccount.Id))
                Positions.Add(PositionRow.From(position));
            var history = new List<TradeRow>();
            foreach (var trade in _repository.ListTrades(ActiveAccount.Id))
                history.Add(TradeRow.From(trade));
            foreach (var cash in _repository.ListCashMovements(ActiveAccount.Id))
                history.Add(TradeRow.FromCash(cash));
            foreach (var row in history.OrderByDescending(item => item.CreatedAt))
                Trades.Add(row);
            foreach (var order in _repository.ListPending(ActiveAccount.Id))
                PendingOrders.Add(PendingOrderRow.From(order));
        }

        // Evita el parpadeo a "—" al encolar/ejecutar: reaplicar última cotización y RSI.
        ReapplyCachedMarket();
        RestoreSelectedPosition(keepId, keepSymbol);
        ApplyHistoryFilter();
        NotifyLists();
        RefreshHeader();
        UpdatePositionHint();
        RecalculatePreview();
        OnPropertyChanged(nameof(QuantityFieldLabel));
    }

    private void RestoreSelectedPosition(long? positionId, string? symbol)
    {
        if (positionId is null && string.IsNullOrWhiteSpace(symbol))
            return;

        PositionRow? match = null;
        if (positionId is long id)
            match = Positions.FirstOrDefault(row => row.Id == id);
        if (match is null && !string.IsNullOrWhiteSpace(symbol))
            match = Positions.FirstOrDefault(row =>
                row.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));

        if (!ReferenceEquals(SelectedPosition, match))
            SelectedPosition = match;
    }

    private void ReapplyCachedMarket()
    {
        foreach (var row in Positions)
        {
            if (TryQuote(row.Symbol, out var quote))
                row.ApplyMarket(quote);
            if (TryRsi(row.Symbol, out var rsi))
                row.ApplyRsi(rsi);
            row.ApplyFx(UsdMxnRate);
        }
    }

    private bool TryRsi(string symbol, out double? rsi)
    {
        if (_lastRsi.TryGetValue(symbol, out rsi))
            return true;
        var normalized = YahooMarket.Normalize(symbol);
        if (normalized is not null && _lastRsi.TryGetValue(normalized, out rsi))
            return true;
        rsi = null;
        return false;
    }

    partial void OnSelectedPositionChanged(PositionRow? value)
    {
        OnPropertyChanged(nameof(HasChartSymbol));
        if (value is null)
        {
            PositionRangeCard.HasSelection = false;
            PositionRangeCard.Symbol = "";
            PositionRangeCard.CompanyName = "";
            PositionRangeCard.ChangeText = "—";
            PositionRangeCard.ChangeTone = "Flat";
            PositionRangeCard.DayRange = PriceRangeMark.Empty;
            PositionRangeCard.WeekRange = PriceRangeMark.Empty;
            return;
        }

        SyncRangeCard(PositionRangeCard, value.Symbol, value.CompanyName);
    }

    [RelayCommand]
    private void ShowDailyChart() => OpenPositionChart(false);

    [RelayCommand]
    private void ShowIntradayChart() => OpenPositionChart(true);

    [RelayCommand]
    private void ShowPositionDayRange() => OpenPositionRange(year: false);

    [RelayCommand]
    private void ShowPositionYearRange() => OpenPositionRange(year: true);

    [RelayCommand]
    private void ShowQuoteDayRange() => OpenQuoteRange(year: false);

    [RelayCommand]
    private void ShowQuoteYearRange() => OpenQuoteRange(year: true);

    [RelayCommand]
    private void ShowActiveEquityChart()
    {
        if (ActiveAccount is null)
        {
            SetStatus("Elige una cuenta para ver la evolución del patrimonio.", true);
            return;
        }

        OpenEquityChart(ActiveAccount.Id, ActiveAccount.Name, ActiveAccount.MarketKind);
    }

    [RelayCommand]
    private void ShowEquityChart(PerformanceRow? row)
    {
        if (row is null)
            return;
        OpenEquityChart(row.AccountId, row.Name, row.MarketKind);
    }

    [RelayCommand]
    private void ShowAllEquityCharts()
    {
        if (Accounts.Count == 0)
        {
            SetStatus("Crea al menos un portafolio para comparar su evolución.", true);
            return;
        }

        var accounts = Accounts
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(item => (item.Id, item.Name, item.MarketKind))
            .ToList();
        AllEquityChartWindow.Present(
            _repository,
            _yahoo,
            accounts,
            accountId =>
            {
                var data = _repository.GetAccount(accountId);
                return data is null ? (0m, 0m, 0m) : MeasureAccount(data);
            });
    }

    private void OpenEquityChart(long accountId, string name, string marketKind)
    {
        EquityChartWindow.Present(
            accountId,
            name,
            marketKind,
            _repository,
            _yahoo,
            () =>
            {
                var data = _repository.GetAccount(accountId);
                return data is null ? (0m, 0m, 0m) : MeasureAccount(data);
            });
    }

    [RelayCommand]
    private void ShowQuoteDailyChart() => OpenQuoteChart(false);

    [RelayCommand]
    private void ShowQuoteIntradayChart() => OpenQuoteChart(true);

    private void OpenPositionChart(bool intraday)
    {
        if (SelectedPosition is null)
            return;
        PriceChartWindow.Present(SelectedPosition.Symbol, SelectedPosition.CompanyName, intraday, _yahoo);
    }

    private void OpenQuoteChart(bool intraday)
    {
        if (CurrentQuote is null)
            return;
        PriceChartWindow.Present(CurrentQuote.Symbol, CurrentQuote.CompanyName, intraday, _yahoo);
    }

    private void OpenPositionRange(bool year)
    {
        if (SelectedPosition is null)
            return;
        SyncRangeCard(PositionRangeCard, SelectedPosition.Symbol, SelectedPosition.CompanyName);
        PriceRangeWindow.Present(PositionRangeCard, year);
    }

    private void OpenQuoteRange(bool year)
    {
        if (CurrentQuote is null)
            return;
        SyncRangeCard(QuoteRangeCard, CurrentQuote.Symbol, CurrentQuote.CompanyName, CurrentQuote);
        PriceRangeWindow.Present(QuoteRangeCard, year);
    }

    private void SyncRangeCard(
        PriceRangeCard card,
        string symbol,
        string? company,
        PesosQuote? quote = null)
    {
        if (quote is null)
            TryQuote(symbol, out quote!);
        if (quote is null)
        {
            card.HasSelection = false;
            card.Symbol = symbol;
            card.CompanyName = company ?? "";
            card.ChangeText = "—";
            card.ChangeTone = "Flat";
            card.DayRange = PriceRangeMark.Empty;
            card.WeekRange = PriceRangeMark.Empty;
            return;
        }

        card.HasSelection = true;
        card.Symbol = quote.Symbol;
        card.CompanyName = string.IsNullOrWhiteSpace(quote.CompanyName) ? company ?? quote.Symbol : quote.CompanyName;
        var change = quote.PreviousMxn is decimal previous && previous > 0
            ? (quote.PriceMxn - previous) / previous * 100m
            : (decimal?)null;
        card.ChangeText = change is null ? "—" : MxFormat.SignedPercent(change.Value);
        card.ChangeTone = change is > 0 ? "Up" : change is < 0 ? "Down" : "Flat";
        card.DayRange = BuildRange(
            quote.DayLowOriginal, quote.DayHighOriginal, quote.PriceOriginal,
            quote.DayLowMxn, quote.DayHighMxn, quote.PriceMxn,
            quote.Currency, quote.InPesos);
        card.WeekRange = BuildRange(
            quote.FiftyTwoWeekLowOriginal, quote.FiftyTwoWeekHighOriginal, quote.PriceOriginal,
            quote.FiftyTwoWeekLowMxn, quote.FiftyTwoWeekHighMxn, quote.PriceMxn,
            quote.Currency, quote.InPesos);
    }

    private static PriceRangeMark BuildRange(
        decimal? lowNative,
        decimal? highNative,
        decimal priceNative,
        decimal? lowMxn,
        decimal? highMxn,
        decimal priceMxn,
        string currency,
        bool inPesos)
    {
        var unit = string.IsNullOrWhiteSpace(currency) ? "MXN" : currency.Trim().ToUpperInvariant();
        var hasRange = lowNative is decimal lo && highNative is decimal hi && hi >= lo && lo > 0;
        var marker = 0.5;
        var hasMarker = false;
        if (hasRange && priceNative > 0 && lowNative is decimal lo2 && highNative is decimal hi2)
        {
            hasMarker = true;
            marker = hi2 > lo2 ? Math.Clamp((double)((priceNative - lo2) / (hi2 - lo2)), 0, 1) : 0.5;
        }

        string Native(decimal? value) =>
            value is decimal number ? MxFormat.Plain(number) + " " + unit : "—";
        string Mxn(decimal? value) =>
            inPesos
                ? "—"
                : value is decimal number ? MxFormat.Price(number) + " MXN" : "—";

        return new PriceRangeMark
        {
            LowNativeText = Native(lowNative),
            LowMxnText = Mxn(lowMxn),
            HighNativeText = Native(highNative),
            HighMxnText = Mxn(highMxn),
            CurrentNativeText = Native(priceNative),
            CurrentMxnText = Mxn(priceMxn),
            HasRange = hasRange,
            HasMarker = hasMarker,
            Marker = marker
        };
    }

    [RelayCommand]
    private void OpenComposition()
    {
        var rows = Positions
            .Select(position => (position, value: position.MarketValue ?? position.TotalCost))
            .Where(item => item.value > 0)
            .OrderByDescending(item => item.value)
            .ToList();
        if (rows.Count == 0)
        {
            SetStatus("No hay posiciones con valor para armar la composición.", true);
            return;
        }

        var total = rows.Sum(item => item.value);
        const double radius = 130;
        var cursor = -90d;
        var slices = new List<CompositionSlice>(rows.Count);
        for (var index = 0; index < rows.Count; index++)
        {
            var (position, value) = rows[index];
            var sweep = index == rows.Count - 1
                ? 270d - cursor
                : (double)(value / total) * 360d;
            var percent = value / total * 100m;
            var weight = percent.ToString("0.0", MxFormat.Culture) + "%";
            var colors = SlicePalette[index % SlicePalette.Length];
            slices.Add(new CompositionSlice
            {
                Symbol = position.Symbol,
                WeightText = weight,
                ToolTipText = position.Symbol + " · " + position.CompanyName + " · " + weight,
                Geometry = PieGeometry.Slice(cursor, sweep, radius),
                Fill = Gradient(colors.Light, colors.Deep),
                Swatch = Swatch(colors.Light, colors.Deep)
            });
            cursor += sweep;
        }

        _composition?.Close();
        var window = new CompositionWindow(slices)
        {
            Owner = Application.Current.MainWindow
        };
        window.Closed += (_, _) => _composition = null;
        _composition = window;
        window.Show();
    }

    private static Brush Gradient(Color light, Color deep)
    {
        var brush = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.32, 0.28),
            Center = new Point(0.5, 0.5),
            RadiusX = 0.85,
            RadiusY = 0.85
        };
        brush.GradientStops.Add(new GradientStop(light, 0));
        brush.GradientStops.Add(new GradientStop(deep, 1));
        brush.Freeze();
        return brush;
    }

    private static Brush Swatch(Color light, Color deep)
    {
        var brush = new LinearGradientBrush(light, deep, new Point(0, 0), new Point(1, 1));
        brush.Freeze();
        return brush;
    }

    private void RefreshHeader()
    {
        if (ActiveAccount is null)
        {
            CashText = "—";
            MarketValueText = "—";
            EquityText = "—";
            EquityBreakdownText = "";
            ReturnText = "—";
            ReturnSinceText = "";
            AnnualizedText = "";
            ShowAnnualized = false;
            ShowReturnHint = false;
            ShowReserved = false;
            ReservedText = "";
            OpenPnlText = "—";
            RealizedPnlText = "—";
            RealizedPnlTone = "Flat";
            InvestedText = "—";
            ReturnTone = "Flat";
            WindowTitle = "SimTrade";
            RebuildPerformanceSummaries();
            return;
        }

        decimal market = 0;
        decimal open = 0;
        var priced = false;
        foreach (var row in Positions)
        {
            market += row.MarketValue ?? row.TotalCost;
            if (row.Unrealized is decimal pnl)
            {
                open += pnl;
                priced = true;
            }
        }

        var netDeposits = _repository.NetDeposits(ActiveAccount.Id);
        var invested = PortfolioMath.InvestedCapital(ActiveAccount.InitialCash, netDeposits);
        var equity = ActiveAccount.Cash + market;
        var profit = equity - invested;
        var percent = invested == 0 ? 0 : profit / invested * 100m;
        // Cash ya excluye lo apartado en compras en cola (hard hold).
        CashText = MxFormat.Money(ActiveAccount.Cash);
        UpdateReservedBanner();
        MarketValueText = MxFormat.Money(market);
        EquityText = MxFormat.Money(equity);
        EquityBreakdownText = "Efectivo + mercado";
        InvestedText = MxFormat.Money(invested);
        ReturnText = MxFormat.SignedMoney(profit) + "    " + MxFormat.SignedPercent(percent);
        var age = DateTime.UtcNow - ActiveAccount.CreatedAt.ToUniversalTime();
        if (age < TimeSpan.Zero)
            age = TimeSpan.Zero;
        ReturnSinceText = MxFormat.Since(ActiveAccount.CreatedAt);
        ShowAnnualized = PortfolioMath.TryAnnualizedPercent(equity, invested, age, out var annualized);
        AnnualizedText = ShowAnnualized ? "Anualizado  " + MxFormat.SignedPercent(annualized) : "";
        ShowReturnHint = !ShowAnnualized && invested > 0;
        OpenPnlText = Positions.Count == 0 || priced ? MxFormat.SignedMoney(open) : "—";
        var realized = _repository.SumRealizedPnl(ActiveAccount.Id);
        RealizedPnlText = MxFormat.SignedMoney(realized);
        RealizedPnlTone = realized > 0 ? "Up" : realized < 0 ? "Down" : "Flat";
        ReturnTone = profit > 0 ? "Up" : profit < 0 ? "Down" : "Flat";
        WindowTitle = "SimTrade — " + ActiveAccount.Name;
        OnPropertyChanged(nameof(QuantityFieldLabel));

        foreach (var row in Positions)
        {
            var value = row.MarketValue ?? row.TotalCost;
            row.SetWeight(market == 0 ? null : value / market * 100m);
        }

        RebuildPerformanceSummaries();
        RebuildDayRanking();
        RebuildTradingStats();
    }

    private void RebuildPerformanceSummaries()
    {
        PerformanceSummaries.Clear();
        foreach (var account in Accounts.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var data = _repository.GetAccount(account.Id);
            if (data is null)
                continue;

            var positions = _repository.ListPositions(account.Id);
            decimal market = 0;
            foreach (var position in positions)
            {
                if (TryQuote(position.Symbol, out var quote))
                    market += PortfolioMath.RoundMoney(position.Quantity * quote.PriceMxn);
                else
                    market += position.TotalCost;
            }

            var cashAvailable = data.Cash;
            var netDeposits = _repository.NetDeposits(data.Id);
            var invested = PortfolioMath.InvestedCapital(data.InitialCash, netDeposits);
            var equity = data.Cash + market;
            var profit = equity - invested;
            var percent = invested == 0 ? 0 : profit / invested * 100m;
            var age = DateTime.UtcNow - data.CreatedAt.ToUniversalTime();
            if (age < TimeSpan.Zero)
                age = TimeSpan.Zero;
            var showAnnualized = PortfolioMath.TryAnnualizedPercent(equity, invested, age, out var annualized);

            var assetCount = positions.Count;
            PerformanceSummaries.Add(new PerformanceRow
            {
                AccountId = data.Id,
                Name = data.Name,
                Icon = data.Icon,
                Notes = data.Notes,
                IsActive = ActiveAccount?.Id == account.Id,
                MarketKind = data.MarketKind,
                AssetCount = assetCount,
                AssetCountText = assetCount == 1
                    ? "1 activo"
                    : assetCount.ToString(MxFormat.Culture) + " activos",
                CashText = MxFormat.Money(cashAvailable),
                MarketValueText = MxFormat.Money(market),
                EquityText = MxFormat.Money(equity),
                ReturnText = MxFormat.SignedMoney(profit) + "    " + MxFormat.SignedPercent(percent),
                ReturnSinceText = MxFormat.Since(data.CreatedAt),
                AnnualizedText = showAnnualized ? "Anualizado  " + MxFormat.SignedPercent(annualized) : "",
                ShowAnnualized = showAnnualized,
                ShowReturnHint = !showAnnualized && invested > 0,
                ReturnTone = profit > 0 ? "Up" : profit < 0 ? "Down" : "Flat",
                EquityValue = equity,
                InvestedValue = invested,
                ProfitValue = profit,
                CashValue = cashAvailable,
                MarketValue = market
            });
        }

        RebuildConsolidatedTotals();
        OnPropertyChanged(nameof(HasPerformance));
        OnPropertyChanged(nameof(ShowEmptyPerformance));
    }

    private void RebuildConsolidatedTotals()
    {
        if (PerformanceSummaries.Count == 0)
        {
            ConsolidatedEquityText = "—";
            ConsolidatedCashText = "—";
            ConsolidatedMarketText = "—";
            ConsolidatedInvestedText = "—";
            ConsolidatedReturnText = "—";
            ConsolidatedReturnTone = "Flat";
            return;
        }

        var equity = PerformanceSummaries.Sum(row => row.EquityValue);
        var cash = PerformanceSummaries.Sum(row => row.CashValue);
        var market = PerformanceSummaries.Sum(row => row.MarketValue);
        var invested = PerformanceSummaries.Sum(row => row.InvestedValue);
        var profit = equity - invested;
        var percent = invested == 0 ? 0 : profit / invested * 100m;
        ConsolidatedEquityText = MxFormat.Money(equity);
        ConsolidatedCashText = MxFormat.Money(cash);
        ConsolidatedMarketText = MxFormat.Money(market);
        ConsolidatedInvestedText = MxFormat.Money(invested);
        ConsolidatedReturnText = MxFormat.SignedMoney(profit) + "    " + MxFormat.SignedPercent(percent);
        ConsolidatedReturnTone = profit > 0 ? "Up" : profit < 0 ? "Down" : "Flat";
    }

    private bool TryQuote(string symbol, out PesosQuote quote)
    {
        if (_lastQuotes.TryGetValue(symbol, out quote!))
            return true;
        var normalized = YahooMarket.Normalize(symbol);
        if (normalized is not null && _lastQuotes.TryGetValue(normalized, out quote!))
            return true;
        quote = null!;
        return false;
    }

    private void CaptureDailyEquitySnapshots(IReadOnlyDictionary<string, PesosQuote> quotes)
    {
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, MexicoZone);
        var today = DateOnly.FromDateTime(nowLocal);
        var stocksOpen = DetectStocksMarketOpen(quotes);

        if (_lastLocalDate is DateOnly previous && previous < today)
            SaveAccountSnapshots(previous, cryptoOnly: true, overwrite: false);

        if (_stocksMarketOpen == true && stocksOpen == false)
        {
            SaveAccountSnapshots(today, stocksOnly: true, overwrite: false);
            CancelStocksLimitOrdersAtClose();
        }
        else if (stocksOpen == false && nowLocal.TimeOfDay >= new TimeSpan(15, 0, 0))
            SaveAccountSnapshots(today, stocksOnly: true, overwrite: false);

        if (nowLocal.TimeOfDay >= new TimeSpan(23, 45, 0))
            SaveAccountSnapshots(today, cryptoOnly: true, overwrite: true);

        _stocksMarketOpen = stocksOpen;
        _lastLocalDate = today;
    }

    private void CancelStocksLimitOrdersAtClose()
    {
        var cancelled = _repository.CancelLimitOrdersForStocksAccounts();
        if (cancelled <= 0)
            return;

        if (ActiveAccount is { IsCrypto: false })
            RefreshActiveSnapshot();
        else
        {
            OnPropertyChanged(nameof(HasPending));
            OnPropertyChanged(nameof(PendingCount));
        }

        SetStatus(cancelled == 1
            ? "Se canceló 1 orden limitada de Bolsa al cierre del mercado."
            : "Se cancelaron " + cancelled.ToString(MxFormat.Culture) + " órdenes limitadas de Bolsa al cierre del mercado.", false);
    }

    private static bool DetectStocksMarketOpen(IReadOnlyDictionary<string, PesosQuote> quotes)
    {
        foreach (var quote in quotes.Values)
        {
            if (IsCryptoQuote(quote))
                continue;
            if (quote.MarketOpen)
                return true;
        }

        return false;
    }

    private static bool IsCryptoQuote(PesosQuote quote)
    {
        var instrument = quote.Instrument.Trim().ToUpperInvariant();
        return instrument == "CRYPTOCURRENCY"
               || quote.Symbol.Contains("-USD", StringComparison.OrdinalIgnoreCase)
               || quote.Symbol.EndsWith("-MXN", StringComparison.OrdinalIgnoreCase);
    }

    private void SaveAccountSnapshots(
        DateOnly asOfDate,
        bool stocksOnly = false,
        bool cryptoOnly = false,
        bool overwrite = false)
    {
        foreach (var account in Accounts)
        {
            var data = _repository.GetAccount(account.Id);
            if (data is null)
                continue;
            if (stocksOnly && data.IsCrypto)
                continue;
            if (cryptoOnly && !data.IsCrypto)
                continue;
            if (!overwrite && _repository.HasEquitySnapshot(account.Id, asOfDate))
                continue;

            var (cash, market, equity) = MeasureAccount(data);
            _repository.UpsertEquitySnapshot(account.Id, asOfDate, equity, market, cash, overwrite);
        }
    }

    private (decimal Cash, decimal Market, decimal Equity) MeasureAccount(AccountData data)
    {
        var positions = _repository.ListPositions(data.Id);
        decimal market = 0;
        foreach (var position in positions)
        {
            if (TryQuote(position.Symbol, out var quote))
                market += PortfolioMath.RoundMoney(position.Quantity * quote.PriceMxn);
            else
                market += position.TotalCost;
        }

        return (data.Cash, market, data.Cash + market);
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

    private void RecalculatePreview()
    {
        PreviewWarning = "";
        PreviewVisible = false;
        PreviewCashAfterTone = "Flat";
        PreviewCashAvailableText = "";
        if (ActiveAccount is null || !MxFormat.TryParse(QuantityText, out var quantity) || quantity <= 0
            || !MxFormat.TryParse(PriceText, out var price) || price <= 0)
        {
            NotifyTradeCommand();
            return;
        }

        var crypto = ActiveAccount.IsCrypto;
        quantity = PortfolioMath.RoundQuantity(quantity, crypto);
        price = decimal.Round(price, 4, MidpointRounding.AwayFromZero);
        var position = FindPosition(SymbolText);
        var oldQuantity = position?.Quantity ?? 0;
        var oldTotal = position?.TotalCost ?? 0;

        var reserved = ReservedBuyCash();
        var freeCash = ActiveAccount.Cash;
        var freeShares = oldQuantity - ReservedSellQty(SymbolText);
        PreviewCashAvailableText = reserved > 0
            ? MxFormat.Money(freeCash) + " disponible · " + MxFormat.Money(reserved) + " ya apartados en cola"
            : MxFormat.Money(freeCash);
        if (IsBuy)
        {
            var math = PortfolioMath.ComputeBuy(oldQuantity, oldTotal, quantity, price, ActiveAccount.BuyCommissionPercent);
            var cashAfter = freeCash - math.Debit;
            PreviewCaption = PreviewSideCaption(true);
            PreviewGrossText = MxFormat.Money(math.Gross);
            PreviewCommissionCaption = "Comisión de compra (" + MxFormat.Percent(ActiveAccount.BuyCommissionPercent) + ")";
            PreviewCommissionText = MxFormat.Money(math.Commission);
            PreviewTotalCaption = NeedsTriggerPrice || !CanTradeNow(CurrentQuote) ? "Efectivo a descontar" : "Total a pagar";
            PreviewTotalText = MxFormat.Money(math.Debit);
            PreviewCashAfterText = MxFormat.Money(cashAfter);
            PreviewCashAfterTone = cashAfter < 0 ? "Down" : cashAfter > 0 ? "Up" : "Flat";
            PreviewAverageText = MxFormat.Price(math.NewAverage);
            PreviewQuantityText = MxFormat.Quantity(math.NewQuantity);
            if (cashAfter < 0)
            {
                var maxShares = PortfolioMath.MaxAffordableQuantity(freeCash, price, ActiveAccount.BuyCommissionPercent, crypto);
                PreviewWarning = reserved > 0
                    ? "No hay efectivo disponible suficiente. Ya hay " + MxFormat.Money(reserved) + " descontados por compras en cola."
                    : "No hay efectivo disponible suficiente. Disponible: " + MxFormat.Money(freeCash) + ".";
                if (maxShares > 0)
                    PreviewWarning += " Cabén como máximo " + MxFormat.Quantity(maxShares) + " a este precio.";
            }
        }
        else if (position is null || quantity > freeShares)
        {
            PreviewCaption = PreviewSideCaption(false);
            PreviewGrossText = "—";
            PreviewCommissionCaption = "Comisión de venta (" + MxFormat.Percent(ActiveAccount.SellCommissionPercent) + ")";
            PreviewCommissionText = "—";
            PreviewTotalCaption = "Total a recibir";
            PreviewTotalText = "—";
            PreviewCashAfterText = MxFormat.Money(ActiveAccount.Cash);
            PreviewAverageText = position is null ? "—" : MxFormat.Price(position.AverageCost);
            PreviewQuantityText = position is null ? "0" : MxFormat.Quantity(freeShares) + " libres";
            PreviewWarning = position is null
                ? "No tienes este activo. Cambia a Compra para adquirirlo con tu efectivo disponible ("
                  + MxFormat.Money(freeCash) + ")."
                : ReservedSellQty(SymbolText) > 0
                    ? "No hay suficiente cantidad libre. Parte está apartada en la cola."
                    : "No hay suficiente cantidad para esta venta.";
            PreviewVisible = true;
            NotifyTradeCommand();
            return;
        }
        else
        {
            var math = PortfolioMath.ComputeSell(oldQuantity, oldTotal, quantity, price, ActiveAccount.SellCommissionPercent);
            PreviewCaption = PreviewSideCaption(false);
            PreviewGrossText = MxFormat.Money(math.Gross);
            PreviewCommissionCaption = "Comisión de venta (" + MxFormat.Percent(ActiveAccount.SellCommissionPercent) + ")";
            PreviewCommissionText = MxFormat.Money(math.Commission);
            PreviewTotalCaption = "Total a recibir · realizado " + MxFormat.SignedMoney(math.RealizedPnl);
            PreviewTotalText = MxFormat.Money(math.Credit);
            PreviewCashAfterText = MxFormat.Money(ActiveAccount.Cash + math.Credit);
            PreviewCashAfterTone = math.RealizedPnl >= 0 ? "Up" : "Down";
            PreviewAverageText = math.NewQuantity == 0 ? "Se cierra la posición" : MxFormat.Price(math.NewAverage);
            PreviewQuantityText = MxFormat.Quantity(math.NewQuantity);
        }

        PreviewVisible = true;
        NotifyTradeCommand();
    }

    private void UpdatePositionHint()
    {
        var position = FindPosition(SymbolText);
        if (position is null)
        {
            PositionHint = "Sin posición abierta en este símbolo.";
            return;
        }

        var held = ReservedSellQty(position.Symbol);
        var free = position.Quantity - held;
        PositionHint = held > 0
            ? $"Posición actual: {MxFormat.Quantity(position.Quantity)} acciones · libres {MxFormat.Quantity(free)} · costo promedio {MxFormat.Price(position.AverageCost)}"
            : $"Posición actual: {MxFormat.Quantity(position.Quantity)} acciones · costo promedio {MxFormat.Price(position.AverageCost)} · invertido {MxFormat.Money(position.TotalCost)}";
    }

    private (string Text, bool Failed)? SettlePending(IReadOnlyDictionary<string, PesosQuote> map)
    {
        if (ActiveAccount is null || PendingOrders.Count == 0)
            return null;

        var filled = 0;
        var failed = 0;
        string? lastFail = null;
        string? lastNotice = null;
        foreach (var order in PendingOrders.ToList())
        {
            if (order.IsLimit)
                continue;
            if (!map.TryGetValue(order.Symbol, out var quote) || !CanTradeNow(quote))
                continue;

            var priceMxn = decimal.Round(quote.SessionOpenMxn ?? quote.PriceMxn, 4, MidpointRounding.AwayFromZero);
            var quotePrice = quote.SessionOpenOriginal ?? quote.PriceOriginal;
            decimal? fx = quote.InPesos ? null : quote.FxRate;
            var company = string.IsNullOrWhiteSpace(quote.CompanyName) ? order.CompanyName : quote.CompanyName;
            var result = order.IsBuy
                ? _repository.Buy(ActiveAccount.Id, order.Symbol, company, order.Currency, order.Quantity, priceMxn, quotePrice, fx, order.Id)
                : _repository.Sell(ActiveAccount.Id, order.Symbol, company, order.Currency, order.Quantity, priceMxn, quotePrice, fx, order.Id);
            if (result.Ok)
            {
                filled++;
                lastNotice = FillNotice(order.IsBuy, order.Symbol, order.Quantity, priceMxn, "Mercado al abrir");
            }
            else
            {
                failed++;
                lastFail = result.Message;
            }
        }

        if (filled == 0 && failed == 0)
            return null;

        RefreshActiveSnapshot();
        if (filled > 0)
        {
            AppNotifier.Show(filled == 1 && lastNotice is not null
                ? lastNotice
                : filled.ToString(MxFormat.Culture) + " órdenes de mercado ejecutadas al abrir.");
        }

        if (filled > 0 && failed == 0)
        {
            return (filled == 1
                ? "Se ejecutó 1 orden de mercado de la cola al abrir."
                : "Se ejecutaron " + filled.ToString(MxFormat.Culture) + " órdenes de mercado de la cola al abrir.", false);
        }

        if (filled > 0)
            return ("Se ejecutaron " + filled.ToString(MxFormat.Culture) + " órdenes. Otras siguen en cola. " + lastFail, true);

        return (lastFail ?? "No se pudo ejecutar la cola al abrir.", true);
    }

    private (string Text, bool Failed)? MatchLimitOrders(IReadOnlyDictionary<string, PesosQuote> map)
    {
        var pending = _repository.ListAllPending();
        if (pending.Count == 0)
            return null;

        var filled = 0;
        var failed = 0;
        string? lastFail = null;
        string? lastNotice = null;
        var touchedActive = false;

        foreach (var order in pending)
        {
            if (!order.IsLimit || order.LimitPriceMxn is not decimal limit || limit <= 0)
                continue;
            if (!map.TryGetValue(order.Symbol, out var quote))
                continue;

            var account = _repository.GetAccount(order.AccountId);
            if (account is null)
                continue;

            var open = account.IsCrypto || quote.MarketOpen;
            if (!open)
                continue;

            var market = decimal.Round(quote.PriceMxn, 4, MidpointRounding.AwayFromZero);
            if (!PortfolioMath.LimitTouched(order.IsBuy, market, limit))
                continue;

            var fill = PortfolioMath.FillPrice(order.IsBuy, market, limit);
            var quotePrice = quote.PriceOriginal;
            decimal? fx = quote.InPesos ? null : quote.FxRate;
            var company = string.IsNullOrWhiteSpace(quote.CompanyName) ? order.CompanyName : quote.CompanyName;
            var result = order.IsBuy
                ? _repository.Buy(order.AccountId, order.Symbol, company, order.Currency, order.Quantity, fill, quotePrice, fx, order.Id)
                : _repository.Sell(order.AccountId, order.Symbol, company, order.Currency, order.Quantity, fill, quotePrice, fx, order.Id);
            if (result.Ok)
            {
                filled++;
                lastNotice = FillNotice(order.IsBuy, order.Symbol, order.Quantity, fill, "Limitada");
                if (ActiveAccount?.Id == order.AccountId)
                    touchedActive = true;
            }
            else
            {
                failed++;
                lastFail = result.Message;
            }
        }

        if (filled == 0 && failed == 0)
            return null;

        if (touchedActive)
            RefreshActiveSnapshot();
        else if (ActiveAccount is not null)
        {
            OnPropertyChanged(nameof(HasPending));
            OnPropertyChanged(nameof(PendingCount));
        }

        if (filled > 0)
        {
            AppNotifier.Show(filled == 1 && lastNotice is not null
                ? lastNotice
                : filled.ToString(MxFormat.Culture) + " órdenes limitadas ejecutadas al tocar el precio.");
        }

        if (filled > 0 && failed == 0)
        {
            return (filled == 1
                ? "Se ejecutó 1 orden limitada al tocar el precio."
                : "Se ejecutaron " + filled.ToString(MxFormat.Culture) + " órdenes limitadas al tocar el precio.", false);
        }

        if (filled > 0)
            return ("Se ejecutaron " + filled.ToString(MxFormat.Culture) + " órdenes limitadas. " + lastFail, true);

        return (lastFail ?? "No se pudo ejecutar una orden limitada.", true);
    }

    private static void NotifyOrderFilled(bool buy, string symbol, decimal quantity, decimal priceMxn, string kind) =>
        AppNotifier.Show(FillNotice(buy, symbol, quantity, priceMxn, kind));

    private static string FillNotice(bool buy, string symbol, decimal quantity, decimal priceMxn, string kind)
    {
        var side = buy ? "Compra" : "Venta";
        var title = kind switch
        {
            "Limitada" => side + " limitada ejecutada",
            "Mercado al abrir" => side + " de la cola ejecutada",
            _ => side + " ejecutada"
        };
        return title + "\n" + symbol + " · " + MxFormat.Quantity(quantity) + " · " + MxFormat.Price(priceMxn);
    }

    private (string Text, bool Failed)? MatchStopAndTakeProfit(IReadOnlyDictionary<string, PesosQuote> map)
    {
        var pending = _repository.ListAllPending();
        if (pending.Count == 0)
            return null;

        var filled = 0;
        var failed = 0;
        string? lastFail = null;
        string? lastNotice = null;
        var touchedActive = false;

        foreach (var order in pending)
        {
            if (!order.IsStopLoss && !order.IsTakeProfit)
                continue;
            if (order.LimitPriceMxn is not decimal trigger || trigger <= 0)
                continue;
            if (!map.TryGetValue(order.Symbol, out var quote))
                continue;

            var account = _repository.GetAccount(order.AccountId);
            if (account is null)
                continue;

            var open = account.IsCrypto || quote.MarketOpen;
            if (!open)
                continue;

            var market = decimal.Round(quote.PriceMxn, 4, MidpointRounding.AwayFromZero);
            var hit = order.IsStopLoss
                ? PortfolioMath.StopTouched(order.IsBuy, market, trigger)
                : PortfolioMath.LimitTouched(order.IsBuy, market, trigger);
            if (!hit)
                continue;

            var fill = order.IsStopLoss
                ? market
                : PortfolioMath.FillPrice(order.IsBuy, market, trigger);
            var quotePrice = quote.PriceOriginal;
            decimal? fx = quote.InPesos ? null : quote.FxRate;
            var company = string.IsNullOrWhiteSpace(quote.CompanyName) ? order.CompanyName : quote.CompanyName;
            var result = order.IsBuy
                ? _repository.Buy(order.AccountId, order.Symbol, company, order.Currency, order.Quantity, fill, quotePrice, fx, order.Id)
                : _repository.Sell(order.AccountId, order.Symbol, company, order.Currency, order.Quantity, fill, quotePrice, fx, order.Id);
            if (result.Ok)
            {
                filled++;
                lastNotice = FillNotice(order.IsBuy, order.Symbol, order.Quantity, fill, OrderTypes.Label(order.OrderType));
                if (ActiveAccount?.Id == order.AccountId)
                    touchedActive = true;
            }
            else
            {
                failed++;
                lastFail = result.Message;
            }
        }

        if (filled == 0 && failed == 0)
            return null;

        if (touchedActive)
            RefreshActiveSnapshot();
        else if (ActiveAccount is not null)
        {
            OnPropertyChanged(nameof(HasPending));
            OnPropertyChanged(nameof(PendingCount));
        }

        if (filled > 0)
        {
            AppNotifier.Show(filled == 1 && lastNotice is not null
                ? lastNotice
                : filled.ToString(MxFormat.Culture) + " órdenes stop/take profit ejecutadas.");
        }

        if (filled > 0 && failed == 0)
        {
            return (filled == 1
                ? "Se ejecutó 1 orden stop/take profit."
                : "Se ejecutaron " + filled.ToString(MxFormat.Culture) + " órdenes stop/take profit.", false);
        }

        if (filled > 0)
            return ("Se ejecutaron " + filled.ToString(MxFormat.Culture) + " órdenes stop/take profit. " + lastFail, true);

        return (lastFail ?? "No se pudo ejecutar una orden stop/take profit.", true);
    }

    private decimal ReservedBuyCash() =>
        PendingOrders.Where(order => order.IsBuy).Sum(order => order.ReservedMxn);

    private decimal ReservedSellQty(string symbol)
    {
        var normalized = YahooMarket.Normalize(symbol);
        if (normalized is null)
            return 0;
        return PendingOrders
            .Where(order => !order.IsBuy
                            && (order.Symbol.Equals(normalized, StringComparison.OrdinalIgnoreCase)
                                || order.Symbol.Equals(normalized + ".MX", StringComparison.OrdinalIgnoreCase)))
            .Sum(order => order.Quantity);
    }

    private void UpdateReservedBanner()
    {
        var reserved = ReservedBuyCash();
        if (ActiveAccount is null || reserved <= 0)
        {
            ShowReserved = false;
            ReservedText = "";
            return;
        }

        ShowReserved = true;
        ReservedText = "Descontado en cola  " + MxFormat.Money(reserved)
                       + " · disponible  " + MxFormat.Money(ActiveAccount.Cash);
    }

    private void UpdateMarketLine()
    {
        var quote = CurrentQuote;
        if (quote is null)
        {
            ShowFx = false;
            ShowSession = false;
            FxLine = "";
            OriginLine = "";
            SessionText = "";
            MarketLine = ActiveAccount is { IsCrypto: true }
                ? "Busca una criptomoneda (por ejemplo BTC-USD o ETH-USD). El precio se muestra en pesos."
                : "Busca una acción o un ETF. Si cotiza en dólares, el precio se muestra en pesos.";
            return;
        }

        var place = string.IsNullOrWhiteSpace(quote.Exchange) ? quote.Symbol : quote.Exchange;
        ShowSession = true;
        SessionText = ActiveAccount is { IsCrypto: true }
            ? NeedsTriggerPrice
                ? "Mercado crypto · 24/7 · la orden permanece hasta tocar el precio o cancelarla"
                : "Mercado crypto · disponible 24/7 · la orden se asigna al precio de ahora"
            : NeedsTriggerPrice
                ? CanTradeNow(quote)
                    ? OrderTypes.IsLimit(OrderKind)
                        ? "Mercado abierto · la limitada se ejecuta al tocar el precio; si no, se cancela al cierre"
                        : "Mercado abierto · stop/take profit se ejecuta al tocar el precio y permanece si no se activa"
                    : OrderTypes.IsLimit(OrderKind)
                        ? "Mercado cerrado · la limitada queda en cola y se cancela si no se ejecuta en la sesión"
                        : "Mercado cerrado · stop/take profit queda en cola y no se cancela al cierre"
                : CanTradeNow(quote)
                    ? "Mercado abierto · la orden se asigna al precio de ahora"
                    : "Mercado cerrado · la orden de mercado queda en cola y se asigna al abrir";
        if (quote.InPesos)
        {
            ShowFx = false;
            FxLine = "";
            OriginLine = "";
            MarketLine = $"{quote.CompanyName} · {place} · cotiza en pesos mexicanos a {MxFormat.Price(quote.PriceMxn)}";
            return;
        }

        ShowFx = true;
        MarketLine = $"{quote.CompanyName} · {place}";
        OriginLine = $"Precio de mercado: {MxFormat.Plain(quote.PriceOriginal)} {quote.Currency}   →   {MxFormat.Price(quote.PriceMxn)} MXN";
        FxLine = $"Tipo de cambio al momento: 1 {quote.Currency} = {MxFormat.Fx(quote.FxRate ?? 0)} MXN";
    }

    private bool CanTradeNow(PesosQuote? quote) =>
        ActiveAccount is { IsCrypto: true } || quote is { MarketOpen: true };

    private bool InstrumentAllowed(PesosQuote quote, out string error)
    {
        var instrument = quote.Instrument.Trim().ToUpperInvariant();
        if (instrument is "CURRENCY" or "INDEX" or "FUTURE" or "FUTURES" or "OPTION")
        {
            error = "Ese símbolo no se puede operar aquí.";
            return false;
        }

        var crypto = string.Equals(instrument, "CRYPTOCURRENCY", StringComparison.Ordinal)
                     || quote.Symbol.Contains("-USD", StringComparison.OrdinalIgnoreCase)
                     || quote.Symbol.EndsWith("-MXN", StringComparison.OrdinalIgnoreCase);

        if (ActiveAccount is { IsCrypto: true })
        {
            if (!crypto)
            {
                error = "Esta cuenta es Crypto. Solo admite criptomonedas (por ejemplo BTC-USD).";
                return false;
            }
        }
        else if (crypto)
        {
            error = "Esta cuenta es de Bolsa. Solo admite acciones y ETFs. Crea una cuenta Crypto para operar cripto.";
            return false;
        }

        error = "";
        return true;
    }

    private PositionRow? FindPosition(string? symbol)
    {
        var normalized = YahooMarket.Normalize(symbol);
        if (normalized is null)
            return null;
        return Positions.FirstOrDefault(item => item.Symbol.Equals(normalized, StringComparison.OrdinalIgnoreCase))
               ?? Positions.FirstOrDefault(item => item.Symbol.Equals(normalized + ".MX", StringComparison.OrdinalIgnoreCase));
    }

    private void ClearSuggestions()
    {
        _suppressSuggest = true;
        ChosenSuggestion = null;
        Suggestions.Clear();
        ShowSuggestions = false;
        _suppressSuggest = false;
    }

    private void NotifyLists()
    {
        OnPropertyChanged(nameof(HasPositions));
        OnPropertyChanged(nameof(ShowSellPositions));
        OnPropertyChanged(nameof(ShowNoSellPositions));
        OnPropertyChanged(nameof(HasTrades));
        OnPropertyChanged(nameof(HasFilteredHistory));
        OnPropertyChanged(nameof(HasPending));
        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(ShowEmptyPortfolio));
        OnPropertyChanged(nameof(ShowEmptyHistory));
        OnPropertyChanged(nameof(ShowEmptyFilteredHistory));
        OnPropertyChanged(nameof(ShowNoAccount));
        OnPropertyChanged(nameof(HistoryPageCount));
        OnPropertyChanged(nameof(HistoryPageText));
        OnPropertyChanged(nameof(ShowHistoryPager));
        OnPropertyChanged(nameof(CanHistoryPrev));
        OnPropertyChanged(nameof(CanHistoryNext));
        UpdateReservedBanner();
    }

    private void SetStatus(string message, bool error)
    {
        StatusText = message;
        StatusTone = error ? "Down" : "Up";
    }
}
