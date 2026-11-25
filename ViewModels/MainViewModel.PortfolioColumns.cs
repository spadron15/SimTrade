using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SimTrade.Services;

namespace SimTrade.ViewModels;

public partial class MainViewModel
{
    private bool _portfolioLayoutSuspend;
    private int _portfolioColumnLayoutVersion;

    public ObservableCollection<ColumnOption> PortfolioColumnOptions { get; } = [];

    public int PortfolioColumnLayoutVersion => _portfolioColumnLayoutVersion;

    [ObservableProperty] private bool _isChoosingPortfolioColumns;

    [RelayCommand]
    private void TogglePortfolioColumns() => IsChoosingPortfolioColumns = !IsChoosingPortfolioColumns;

    [RelayCommand]
    private void MovePortfolioColumnUp(ColumnOption? option) => MovePortfolioColumn(option, -1);

    [RelayCommand]
    private void MovePortfolioColumnDown(ColumnOption? option) => MovePortfolioColumn(option, 1);

    [RelayCommand]
    private void ResetPortfolioColumns()
    {
        _portfolioLayoutSuspend = true;
        PortfolioColumnOptions.Clear();
        foreach (var spec in PortfolioColumnCatalog.Configurable)
            PortfolioColumnOptions.Add(new ColumnOption(spec.Key, spec.Title, spec.Hint, spec.DefaultVisible, CommitPortfolioLayout));
        _portfolioLayoutSuspend = false;
        CommitPortfolioLayout();
    }

    private void LoadPortfolioColumns()
    {
        IReadOnlyDictionary<string, (int Position, bool Visible, string? Title, string? Color)> saved;
        try
        {
            saved = _repository.LoadPortfolioColumnLayout();
        }
        catch
        {
            saved = new Dictionary<string, (int Position, bool Visible, string? Title, string? Color)>(StringComparer.OrdinalIgnoreCase);
        }

        var specs = PortfolioColumnCatalog.Configurable;
        var ordered = specs
            .Select((spec, index) =>
            {
                var known = saved.TryGetValue(spec.Key, out var row);
                return (
                    spec,
                    known,
                    position: known ? row.Position : index,
                    visible: known ? row.Visible : spec.DefaultVisible,
                    title: known ? row.Title : null,
                    color: known ? row.Color : null);
            })
            .OrderBy(item => item.known ? 0 : 1)
            .ThenBy(item => item.position)
            .ThenBy(item => Array.FindIndex(specs.ToArray(), spec => spec.Key == item.spec.Key))
            .ToList();

        _portfolioLayoutSuspend = true;
        PortfolioColumnOptions.Clear();
        foreach (var item in ordered)
        {
            PortfolioColumnOptions.Add(new ColumnOption(
                item.spec.Key, item.spec.Title, item.spec.Hint, item.visible, CommitPortfolioLayout, item.title, item.color));
        }
        _portfolioLayoutSuspend = false;
    }

    private void MovePortfolioColumn(ColumnOption? option, int direction)
    {
        if (option is null)
            return;

        var index = PortfolioColumnOptions.IndexOf(option);
        var target = index + direction;
        if (index < 0 || target < 0 || target >= PortfolioColumnOptions.Count)
            return;

        PortfolioColumnOptions.Move(index, target);
        CommitPortfolioLayout();
    }

    private void CommitPortfolioLayout()
    {
        if (_portfolioLayoutSuspend)
            return;

        try
        {
            _repository.SavePortfolioColumnLayout(PortfolioColumnOptions
                .Select((option, index) => (option.Key, index, option.IsVisible, option.CustomTitle, option.CustomColor))
                .ToList());
        }
        catch
        {
            SetStatus("No se pudo guardar la configuración de las columnas.", true);
        }

        _portfolioColumnLayoutVersion++;
        OnPropertyChanged(nameof(PortfolioColumnLayoutVersion));
    }
}
