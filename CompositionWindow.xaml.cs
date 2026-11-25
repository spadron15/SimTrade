using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using SimTrade.ViewModels;

namespace SimTrade;

public partial class CompositionWindow : Window
{
    public CompositionWindow(IReadOnlyList<CompositionSlice> slices)
    {
        Slices = slices;
        InitializeComponent();
        DataContext = this;
    }

    public IReadOnlyList<CompositionSlice> Slices { get; }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }

        base.OnPreviewKeyDown(e);
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
}
