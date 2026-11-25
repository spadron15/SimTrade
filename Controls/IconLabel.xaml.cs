using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SimTrade.Controls;

public enum ButtonGlyph
{
    None,
    Portfolio,
    Trade,
    Performance,
    History,
    Accounts,
    Chart,
    Intraday,
    Pie,
    Search,
    Buy,
    Sell,
    SellAll,
    Market,
    Check,
    Save,
    Use,
    Trash,
    Close,
    AddUser,
    About
}

public partial class IconLabel : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(IconLabel), new PropertyMetadata("", OnTextChanged));

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(ButtonGlyph), typeof(IconLabel), new PropertyMetadata(ButtonGlyph.None, OnGlyphChanged));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(IconLabel), new PropertyMetadata(16d));

    public static readonly DependencyProperty ShowTextProperty = DependencyProperty.Register(
        nameof(ShowText),
        typeof(bool),
        typeof(IconLabel),
        new PropertyMetadata(true, OnShowTextChanged));

    public IconLabel()
    {
        InitializeComponent();
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public ButtonGlyph Glyph
    {
        get => (ButtonGlyph)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public bool ShowText
    {
        get => (bool)GetValue(ShowTextProperty);
        set => SetValue(ShowTextProperty, value);
    }

    private static void OnShowTextChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is IconLabel label)
            label.ApplyCaptionVisibility();
    }

    private void ApplyCaptionVisibility()
    {
        Caption.Visibility = ShowText ? Visibility.Visible : Visibility.Collapsed;
        if (Mark.Parent is FrameworkElement viewbox)
            viewbox.Margin = ShowText ? new Thickness(0, 0, 8, 0) : new Thickness(0);
    }

    private static void OnTextChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not IconLabel label)
            return;
        var text = args.NewValue as string ?? "";
        label.Caption.Text = text;
        System.Windows.Automation.AutomationProperties.SetName(label, text);
        if (System.Windows.Media.VisualTreeHelper.GetParent(label) is System.Windows.UIElement parent)
            System.Windows.Automation.AutomationProperties.SetName(parent, text);
        label.ApplyCaptionVisibility();
    }

    private static void OnGlyphChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is IconLabel label && args.NewValue is ButtonGlyph glyph)
            label.Mark.Data = Geometry.Parse(PathOf(glyph));
    }

    private static string PathOf(ButtonGlyph glyph) => glyph switch
    {
        ButtonGlyph.Portfolio => "M5,6 H11 V4.4 A1,1 0 0 0 10,3.4 H6 A1,1 0 0 0 5,4.4 Z M3,6.6 H13 V13.2 H3 Z M3,9.2 H13",
        ButtonGlyph.Trade => "M3,5.2 H12.2 L9.6,2.6 M13,10.8 H3.8 L6.4,13.4",
        ButtonGlyph.Performance => "M2.4,13.2 V7.6 H4.8 V13.2 Z M6.4,13.2 V4.4 H8.8 V13.2 Z M10.4,13.2 V2.4 H12.8 V13.2 Z",
        ButtonGlyph.History => "M8,2.6 A5.4,5.4 0 1 1 3.4,5.2 M3.2,2.4 V5.4 H6.2 M8,5.2 V8.2 L10.2,9.4",
        ButtonGlyph.Accounts => "M8,8.2 A2.3,2.3 0 1 0 8,3.6 A2.3,2.3 0 1 0 8,8.2 M3.4,13.4 C3.7,10.6 5.5,9.2 8,9.2 C10.5,9.2 12.3,10.6 12.6,13.4",
        ButtonGlyph.AddUser => "M6.2,7.4 A2,2 0 1 0 6.2,3.4 A2,2 0 1 0 6.2,7.4 M2.4,13.2 C2.7,10.8 4.2,9.6 6.2,9.6 C8.2,9.6 9.7,10.8 10,13.2 M12.2,5.2 V9.6 M10,7.4 H14.4",
        ButtonGlyph.Chart => "M1.6,13.4 V2.2 M1.6,13.4 H14.4 M3.2,10.4 L6.2,6.8 L8.8,9 L13.2,3.6",
        ButtonGlyph.Intraday => "M8,2.4 A5.6,5.6 0 1 0 8,13.6 A5.6,5.6 0 1 0 8,2.4 M8,4.8 V8.2 L10.6,9.6",
        ButtonGlyph.Pie => "M8,2.3 A5.7,5.7 0 1 1 7.9,2.3 M8,8 L8,2.6 M8,8 L12.8,10.6",
        ButtonGlyph.Search => "M7.2,7.2 m-3.6,0 a3.6,3.6 0 1 0 7.2,0 a3.6,3.6 0 1 0 -7.2,0 M10.2,10.2 L13.6,13.6",
        ButtonGlyph.Buy => "M8,3 V13 M3,8 H13",
        ButtonGlyph.Sell => "M3,8 H13",
        ButtonGlyph.SellAll => "M4,3.2 L8,7.2 L12,3.2 M4,8.6 L8,12.6 L12,8.6",
        ButtonGlyph.Market => "M8,2.2 V4.6 M8,11.4 V13.8 M2.2,8 H4.6 M11.4,8 H13.8 M8,8 m-2.6,0 a2.6,2.6 0 1 0 5.2,0 a2.6,2.6 0 1 0 -5.2,0",
        ButtonGlyph.Check => "M3,8.4 L6.4,11.8 L13,4.2",
        ButtonGlyph.Save => "M3,2.4 H10.6 L13.6,5.4 V13.6 H3 Z M5.2,2.4 V6 H9.6 V2.4 M5.2,13.6 V9.2 H10.8 V13.6",
        ButtonGlyph.Use => "M8,2.4 A5.6,5.6 0 1 0 8,13.6 A5.6,5.6 0 1 0 8,2.4 M5.2,8.2 L7.2,10.2 L11,6",
        ButtonGlyph.Trash => "M3.4,4.6 H12.6 M6.2,4.6 V3 H9.8 V4.6 M5.2,4.6 L5.8,13.2 H10.2 L10.8,4.6",
        ButtonGlyph.Close => "M4,4 L12,12 M12,4 L4,12",
        ButtonGlyph.About => "M8,2.4 A5.6,5.6 0 1 0 8,13.6 A5.6,5.6 0 1 0 8,2.4 M8,7.2 V11.2 M8,5 m-0.6,0 a0.6,0.6 0 1 0 1.2,0 a0.6,0.6 0 1 0 -1.2,0",
        _ => ""
    };
}
