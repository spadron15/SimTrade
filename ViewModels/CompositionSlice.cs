using System.Windows.Media;

namespace SimTrade.ViewModels;

public sealed class CompositionSlice
{
    public required string Symbol { get; init; }
    public required string WeightText { get; init; }
    public required string ToolTipText { get; init; }
    public required Geometry Geometry { get; init; }
    public required Brush Fill { get; init; }
    public required Brush Swatch { get; init; }
}
