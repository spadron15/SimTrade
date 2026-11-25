namespace SimTrade.Services;

public sealed class ColumnSpec
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    public string? Hint { get; init; }
    public double Width { get; init; }
    public bool RightAlign { get; init; }
    public bool DefaultVisible { get; init; } = true;
    public required string Cell { get; init; }
    public string? TextPath { get; init; }
    public string? SortKey { get; init; }
}
