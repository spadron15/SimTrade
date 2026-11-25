namespace SimTrade.Services;

public static class PortfolioColumnCatalog
{
    public static IReadOnlyList<ColumnSpec> Configurable { get; } =
    [
        Spec("Company", "Empresa", 160, "Plain", "CompanyName", "CompanyName", hint: "Nombre de la empresa o activo."),
        Spec("Price", "Precio", 120, "Plain", "LastPriceText", "LastPrice", hint: "Último precio en la moneda original.", right: true),
        Spec("PriceMxn", "En pesos", 120, "Plain", "PriceMxnText", "MarketPrice", hint: "Precio convertido a pesos.", right: true),
        Spec("Rank", "#", 44, "Plain", "RankText", "DayRank", hint: "Ranking por contribución al P&L del día."),
        Spec("DayChange", "Variación", 100, "Change", "DayChangeText", "DayChangePercent", hint: "Variación del día vs cierre previo.", right: true),
        Spec("DayPnl", "P&L día", 110, "Heat", "DayContributionText", "DayContribution", hint: "Contribución aproximada al P&L del día.", right: true),
        Spec("Rsi", "RSI", 72, "Plain", "RsiText", "Rsi", hint: "RSI diario de 14 periodos.", right: true),
        Spec("Signal", "Señal", 90, "Signal", "RsiSignal", "RsiSignal", hint: "Señal según el RSI."),
        Spec("Opened", "Fecha de compra", 156, "Plain", "OpenedText", "OpenedAt", hint: "Fecha de apertura de la posición."),
        Spec("Quantity", "Cantidad", 110, "Plain", "QuantityText", "Quantity", hint: "Cantidad de la posición.", right: true),
        Spec("Average", "Costo promedio", 140, "Plain", "AverageText", "AverageCost", hint: "Costo promedio en pesos.", right: true),
        Spec("AverageUsd", "Costo prom. USD", 140, "Plain", "AverageUsdText", "AverageUsd", hint: "Costo promedio convertido a dólares con el tipo de cambio USD/MXN.", right: true),
        Spec("Value", "Valor", 130, "Plain", "MarketValueText", "MarketValue", hint: "Valor de mercado en pesos.", right: true),
        Spec("Unrealized", "G/P", 120, "Gain", "UnrealizedText", "Unrealized", hint: "Ganancia o pérdida no realizada en pesos.", right: true),
        Spec("UnrealizedPct", "G/P %", 90, "Gain", "UnrealizedPercentText", "UnrealizedPercent", hint: "Ganancia o pérdida no realizada en porcentaje.", right: true),
        Spec("Weight", "Peso", 84, "Plain", "WeightText", "Weight", hint: "Peso de la posición en el portafolio.", right: true)
    ];

    public static ColumnSpec? Find(string key) =>
        Configurable.FirstOrDefault(spec => spec.Key == key);

    private static ColumnSpec Spec(
        string key,
        string title,
        double width,
        string cell,
        string textPath,
        string sort,
        bool visible = true,
        string? hint = null,
        bool right = false) => new()
    {
        Key = key,
        Title = title,
        Hint = hint,
        Width = width,
        RightAlign = right,
        DefaultVisible = visible,
        Cell = cell,
        TextPath = textPath,
        SortKey = sort
    };
}
