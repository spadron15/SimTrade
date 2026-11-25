using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using SimTrade.Controls;
using SimTrade.Services;
using SimTrade.ViewModels;

namespace SimTrade;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => RebuildPortfolioColumns();

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanged oldVm)
            oldVm.PropertyChanged -= ViewModelOnPropertyChanged;
        if (e.NewValue is INotifyPropertyChanged newVm)
            newVm.PropertyChanged += ViewModelOnPropertyChanged;
        RebuildPortfolioColumns();
    }

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.PortfolioColumnLayoutVersion))
            RebuildPortfolioColumns();
    }

    private void PortfolioColumnColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ColumnColorChoice choice })
            choice.Choose();
    }

    private void ColumnPopupThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is not DependencyObject source)
            return;
        var popup = PopupContaining(source);
        if (popup is null)
            return;

        popup.HorizontalOffset += e.HorizontalChange;
        popup.VerticalOffset += e.VerticalChange;
    }

    private void ColumnPopupResize_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is not DependencyObject source)
            return;
        if (PopupContaining(source)?.Child is not FrameworkElement shell)
            return;

        var minWidth = shell.MinWidth > 0 ? shell.MinWidth : 320;
        var minHeight = shell.MinHeight > 0 ? shell.MinHeight : 280;
        shell.Width = Math.Clamp(shell.ActualWidth + e.HorizontalChange, minWidth, 980);
        shell.Height = Math.Clamp(shell.ActualHeight + e.VerticalChange, minHeight, 900);
    }

    private void ColumnPopupThumb_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (sender is DependencyObject source && PopupContaining(source) is { } popup)
            KeepColumnPopupOnScreen(popup);
    }

    private Popup? PopupContaining(DependencyObject element)
    {
        if (IsInside(PortfolioColumnsPopup.Child as DependencyObject, element))
            return PortfolioColumnsPopup;
        return null;
    }

    private static bool IsInside(DependencyObject? root, DependencyObject element)
    {
        for (var node = element; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, root))
                return true;
        }

        return false;
    }

    private static void KeepColumnPopupOnScreen(Popup popup)
    {
        if (popup.Child is not FrameworkElement child)
            return;
        if (PresentationSource.FromVisual(child)?.CompositionTarget is not { } target)
            return;

        var origin = target.TransformFromDevice.Transform(child.PointToScreen(new Point(0, 0)));
        var width = child.ActualWidth;
        var left = SystemParameters.VirtualScreenLeft;
        var top = SystemParameters.VirtualScreenTop;
        var right = left + SystemParameters.VirtualScreenWidth;
        var bottom = top + SystemParameters.VirtualScreenHeight;
        var dx = 0d;
        var dy = 0d;
        if (origin.X + width < left + 80)
            dx = left + 80 - (origin.X + width);
        if (origin.X > right - 80)
            dx = right - 80 - origin.X;
        if (origin.Y < top)
            dy = top - origin.Y;
        if (origin.Y + 48 > bottom)
            dy = bottom - 48 - origin.Y;
        popup.HorizontalOffset += dx;
        popup.VerticalOffset += dy;
    }

    private void RebuildPortfolioColumns()
    {
        if (PositionsGrid is null || DataContext is not MainViewModel viewModel)
            return;

        PositionsGrid.Columns.Clear();
        PositionsGrid.Columns.Add(CreateSymbolColumn());
        PositionsGrid.Columns.Add(CreateActionsColumn());

        foreach (var option in viewModel.PortfolioColumnOptions)
        {
            if (!option.IsVisible)
                continue;
            var spec = PortfolioColumnCatalog.Find(option.Key);
            if (spec is null)
                continue;
            PositionsGrid.Columns.Add(CreatePortfolioColumn(spec, option.DisplayTitle, ColumnColors.BrushFor(option.CustomColor)));
        }
    }

    private static DataGridTemplateColumn CreateSymbolColumn()
    {
        var column = new DataGridTemplateColumn
        {
            Header = "Símbolo",
            SortMemberPath = "Symbol",
            Width = new DataGridLength(168)
        };
        var template = new DataTemplate();
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        panel.SetValue(FrameworkElement.MarginProperty, new Thickness(10, 0, 0, 0));
        panel.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

        var logo = new FrameworkElementFactory(typeof(LogoBadge));
        logo.SetBinding(LogoBadge.SymbolProperty, new Binding("Symbol"));
        logo.SetValue(LogoBadge.SizeProperty, 28d);
        logo.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding("Symbol"));
        text.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        text.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 0, 0));
        text.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

        panel.AppendChild(logo);
        panel.AppendChild(text);
        template.VisualTree = panel;
        column.CellTemplate = template;
        return column;
    }

    private DataGridTemplateColumn CreateActionsColumn()
    {
        var column = new DataGridTemplateColumn
        {
            Header = "",
            Width = new DataGridLength(248),
            MinWidth = 248,
            CanUserSort = false,
            IsReadOnly = false
        };
        var template = new DataTemplate();
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        panel.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 0, 0));

        var buy = CreateActionButton("SmallPrimary", "PrepareBuyCommand", ButtonGlyph.Buy, "Comprar", new Thickness(0, 0, 6, 0));
        var sell = CreateActionButton("SmallSecondary", "PrepareSellCommand", ButtonGlyph.Sell, "Vender", new Thickness(0));
        panel.AppendChild(buy);
        panel.AppendChild(sell);
        template.VisualTree = panel;
        column.CellTemplate = template;
        return column;
    }

    private FrameworkElementFactory CreateActionButton(
        string styleKey,
        string commandName,
        ButtonGlyph glyph,
        string caption,
        Thickness margin)
    {
        var button = new FrameworkElementFactory(typeof(Button));
        button.SetResourceReference(FrameworkElement.StyleProperty, styleKey);
        button.SetValue(FrameworkElement.MarginProperty, margin);
        button.SetBinding(Button.CommandProperty, new Binding("DataContext." + commandName)
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DataGrid), 1)
        });
        button.SetBinding(Button.CommandParameterProperty, new Binding());

        var label = new FrameworkElementFactory(typeof(IconLabel));
        label.SetValue(IconLabel.GlyphProperty, glyph);
        label.SetValue(IconLabel.TextProperty, caption);
        label.SetValue(IconLabel.SizeProperty, 14d);
        button.AppendChild(label);
        return button;
    }

    private DataGridTemplateColumn CreatePortfolioColumn(ColumnSpec spec, string? title, Brush? background)
    {
        var column = new DataGridTemplateColumn
        {
            Header = string.IsNullOrWhiteSpace(title) ? spec.Title : title,
            SortMemberPath = spec.SortKey,
            Width = new DataGridLength(spec.Width)
        };

        var template = new DataTemplate();
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, background ?? Brushes.Transparent);
        border.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        border.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Stretch);
        border.SetValue(Border.PaddingProperty, new Thickness(0));
        border.SetValue(FrameworkElement.MarginProperty, new Thickness(0));

        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding(spec.TextPath));
        text.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        text.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, spec.RightAlign ? 12 : 6, 0));
        text.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        if (spec.RightAlign)
            text.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Right);

        var styleKey = spec.Cell switch
        {
            "Change" => "ChangeText",
            "Heat" => "HeatText",
            "Gain" => TryFindResource("PnlText") is Style ? "PnlText" : "ReturnText",
            "Signal" => "RsiSignalText",
            _ => null
        };
        if (styleKey is not null)
            text.SetResourceReference(FrameworkElement.StyleProperty, styleKey);
        else if (spec.Key is "Price" or "Rsi")
            text.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);

        border.AppendChild(text);
        template.VisualTree = border;
        column.CellTemplate = template;
        return column;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var useDark = 1;
        DwmSetWindowAttribute(hwnd, 20, ref useDark, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private void SymbolBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not MainViewModel viewModel)
            return;
        viewModel.LookupSymbolCommand.Execute(null);
        e.Handled = true;
    }
}
