using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace SimTrade;

public partial class NoticeWindow : Window
{
    private readonly DispatcherTimer _timer;

    public NoticeWindow()
    {
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
        _timer.Tick += (_, _) => Close();
    }

    public void Present(string body)
    {
        Body.Text = body;
        if (!IsVisible)
            Show();
        UpdateLayout();
        Place();
        _timer.Stop();
        _timer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        base.OnClosed(e);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Place()
    {
        var area = SystemParameters.WorkArea;
        var main = Application.Current?.MainWindow;
        if (main is not null)
        {
            var hwnd = new WindowInteropHelper(main).Handle;
            if (hwnd != IntPtr.Zero && TryWorkArea(hwnd, main, out var work))
                area = work;
        }

        Left = area.Right - ActualWidth - 18;
        Top = area.Bottom - ActualHeight - 18;
    }

    private static bool TryWorkArea(IntPtr hwnd, Visual visual, out Rect area)
    {
        area = default;
        var monitor = MonitorFromWindow(hwnd, 2);
        if (monitor == IntPtr.Zero)
            return false;

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
            return false;

        var source = PresentationSource.FromVisual(visual);
        if (source?.CompositionTarget is null)
            return false;

        var transform = source.CompositionTarget.TransformFromDevice;
        var topLeft = transform.Transform(new Point(info.Work.Left, info.Work.Top));
        var bottomRight = transform.Transform(new Point(info.Work.Right, info.Work.Bottom));
        area = new Rect(topLeft, bottomRight);
        return area.Width > 0 && area.Height > 0;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct RectNative
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public RectNative Monitor;
        public RectNative Work;
        public uint Flags;
    }
}
