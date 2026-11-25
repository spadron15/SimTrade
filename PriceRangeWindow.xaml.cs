using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using SimTrade.ViewModels;

namespace SimTrade;

public partial class PriceRangeWindow : Window
{
    private static PriceRangeWindow? _day;
    private static PriceRangeWindow? _year;
    private bool _yearRange;

    public PriceRangeWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => ApplyVisibleRange();
    }

    public static void Present(PriceRangeCard card, bool year)
    {
        var slot = year ? _year : _day;
        var owner = Application.Current?.MainWindow;
        if (slot is null)
        {
            slot = new PriceRangeWindow
            {
                DataContext = card,
                _yearRange = year,
                Title = year ? "Rango 52 semanas" : "Rango intradía"
            };
            slot.RangeTitle.Text = year ? "Rango de 52 semanas" : "Rango intradía";
            if (owner is { IsLoaded: true })
                slot.Owner = owner;
            slot.Show();
            if (year)
            {
                slot.Left += 36;
                slot.Top += 36;
                _year = slot;
            }
            else
            {
                _day = slot;
            }
        }
        else
        {
            slot.DataContext = card;
            if (slot.WindowState == WindowState.Minimized)
                slot.WindowState = WindowState.Normal;
        }

        slot.ApplyVisibleRange();
        slot.Activate();
    }

    private void ApplyVisibleRange()
    {
        if (DataContext is not PriceRangeCard card)
            return;

        var mark = _yearRange ? card.WeekRange : card.DayRange;
        DayTrack.Visibility = !_yearRange && mark.HasRange ? Visibility.Visible : Visibility.Collapsed;
        WeekTrack.Visibility = _yearRange && mark.HasRange ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Visibility = card.HasSelection && !mark.HasRange ? Visibility.Visible : Visibility.Collapsed;
        PriceNative.Text = mark.CurrentNativeText;
        PriceMxn.Text = mark.CurrentMxnText;

        // Re-bind header price when card updates while open.
        card.PropertyChanged -= OnCardChanged;
        card.PropertyChanged += OnCardChanged;
    }

    private void OnCardChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PriceRangeCard.DayRange)
            or nameof(PriceRangeCard.WeekRange)
            or nameof(PriceRangeCard.HasSelection)
            or null)
        {
            ApplyVisibleRange();
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var useDark = 1;
        DwmSetWindowAttribute(hwnd, 20, ref useDark, sizeof(int));
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is PriceRangeCard card)
            card.PropertyChanged -= OnCardChanged;
        if (ReferenceEquals(_day, this))
            _day = null;
        if (ReferenceEquals(_year, this))
            _year = null;
        base.OnClosed(e);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
