using System.ComponentModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using SimTrade.Services;

namespace SimTrade.ViewModels;

public sealed class ColumnColorChoice : ObservableObject
{
    private readonly ColumnOption _column;

    public ColumnColorChoice(ColumnOption column, ColumnSwatch swatch)
    {
        _column = column;
        Key = swatch.Key;
        Name = swatch.Name;
        Brush = swatch.Brush;
        IsNone = swatch.IsNone;
        column.PropertyChanged += OnColumnChanged;
    }

    public string Key { get; }
    public string Name { get; }
    public Brush Brush { get; }
    public bool IsNone { get; }
    public bool IsSelected => string.Equals(_column.BackgroundKey ?? "", Key, StringComparison.Ordinal);

    public void Choose() => _column.ChooseBackground(string.IsNullOrEmpty(Key) ? null : Key);

    private void OnColumnChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ColumnOption.BackgroundKey))
            OnPropertyChanged(nameof(IsSelected));
    }
}
