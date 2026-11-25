using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SimTrade.Services;

namespace SimTrade.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] private bool _isHelpOpen;
    [ObservableProperty] private string _helpTitle = "";
    [ObservableProperty] private string _helpSubtitle = "";

    public ObservableCollection<HelpSection> HelpSections { get; } = [];

    [RelayCommand]
    private void ShowPageHelp()
    {
        var help = PageHelpCatalog.For(Page);
        HelpTitle = help.Title;
        HelpSubtitle = help.Subtitle;
        HelpSections.Clear();
        foreach (var section in help.Sections)
            HelpSections.Add(section);
        IsHelpOpen = true;
    }

    [RelayCommand]
    private void CloseHelp() => IsHelpOpen = false;
}
