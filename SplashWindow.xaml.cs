using System.Windows;
using System.Windows.Input;

namespace SimTrade;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
        var version = typeof(SplashWindow).Assembly.GetName().Version;
        VersionText.Text = version is null ? "Cargando…" : "Versión " + version.ToString(3);
        Cursor = Cursors.Hand;
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => Close();
}
