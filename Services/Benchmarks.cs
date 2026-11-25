namespace SimTrade.Services;

public sealed record BenchmarkOption(string Symbol, string Label);

public static class Benchmarks
{
    public static IReadOnlyList<BenchmarkOption> All { get; } =
    [
        new("", "Sin benchmark"),
        new("^MXX", "IPC México (^MXX)"),
        new("^GSPC", "S&P 500 (^GSPC)"),
        new("SPY", "SPDR S&P 500 (SPY)"),
        new("QQQ", "Nasdaq-100 (QQQ)"),
        new("BTC-USD", "Bitcoin (BTC-USD)"),
        new("ACWI", "MSCI World (ACWI)")
    ];
}
