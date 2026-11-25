using CommunityToolkit.Mvvm.ComponentModel;
using SimTrade.Services;

namespace SimTrade.ViewModels;

public partial class ColumnOption : ObservableObject
{
    private readonly Action _changed;
    private bool _suspend;
    private string _committedTitle;
    private string? _committedColor;

    public ColumnOption(
        string key,
        string title,
        string? hint,
        bool visible,
        Action changed,
        string? displayTitle = null,
        string? backgroundKey = null)
    {
        Key = key;
        Title = title;
        Hint = hint;
        _changed = changed;
        var shown = string.IsNullOrWhiteSpace(displayTitle) ? title : displayTitle.Trim();
        _committedTitle = shown;
        _suspend = true;
        IsVisible = visible;
        DisplayTitle = shown;
        BackgroundKey = ColumnColors.Normalize(backgroundKey);
        _committedColor = BackgroundKey;
        ColorChoices = ColumnColors.Palette.Select(swatch => new ColumnColorChoice(this, swatch)).ToList();
        _suspend = false;
    }

    public string Key { get; }
    public string Title { get; }
    public string? Hint { get; }

    public string? CustomTitle =>
        string.IsNullOrWhiteSpace(DisplayTitle) || string.Equals(DisplayTitle.Trim(), Title, StringComparison.Ordinal)
            ? null
            : DisplayTitle.Trim();

    public string? CustomColor => ColumnColors.Normalize(BackgroundKey);

    public IReadOnlyList<ColumnColorChoice> ColorChoices { get; }

    public void ChooseBackground(string? key)
    {
        var next = ColumnColors.Normalize(key);
        if (string.Equals(next, BackgroundKey, StringComparison.Ordinal))
            return;
        BackgroundKey = next;
    }

    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private string? _backgroundKey;
    [ObservableProperty] private string _displayTitle = "";

    partial void OnIsVisibleChanged(bool value)
    {
        if (!_suspend)
            _changed();
    }

    partial void OnDisplayTitleChanged(string value)
    {
        if (_suspend)
            return;

        var next = string.IsNullOrWhiteSpace(value) ? Title : value.Trim();
        if (!string.Equals(next, value, StringComparison.Ordinal))
        {
            _suspend = true;
            DisplayTitle = next;
            _suspend = false;
        }

        if (string.Equals(next, _committedTitle, StringComparison.Ordinal))
            return;

        _committedTitle = next;
        _changed();
    }

    partial void OnBackgroundKeyChanged(string? value)
    {
        if (_suspend)
            return;

        var next = ColumnColors.Normalize(value);
        if (!string.Equals(next, value, StringComparison.Ordinal))
        {
            _suspend = true;
            BackgroundKey = next;
            _suspend = false;
        }

        if (string.Equals(next, _committedColor, StringComparison.Ordinal))
            return;

        _committedColor = next;
        _changed();
    }

    public void SetVisible(bool visible)
    {
        _suspend = true;
        IsVisible = visible;
        _suspend = false;
    }
}
