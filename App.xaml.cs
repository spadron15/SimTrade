using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using SimTrade.Data;
using SimTrade.Services;
using SimTrade.ViewModels;

namespace SimTrade;

public partial class App : Application
{
    private MainViewModel? _viewModel;
    private Mutex? _instanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        var culture = new CultureInfo("es-MX");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;

        _instanceMutex = new Mutex(true, "SimTradeSingleInstance", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "SimTrade ya está en ejecución.",
                "SimTrade",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown(0);
            return;
        }

        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var database = new AppDatabase();
        try
        {
            database.Initialize();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "No se pudo preparar la base de datos local.\n\n" + ex.Message,
                "SimTrade",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var repository = new TradeRepository(database);
        repository.EnsureHardCashHoldsMigrated();
        _viewModel = new MainViewModel(repository, new YahooMarket());
        var window = new MainWindow { DataContext = _viewModel };
        MainWindow = window;

        var splash = new SplashWindow();
        var revealed = 0;
        void Reveal()
        {
            if (Interlocked.Exchange(ref revealed, 1) == 1)
                return;
            window.Show();
            if (splash.IsVisible)
                splash.Close();
        }

        splash.Closed += (_, _) => Reveal();
        splash.Show();
        _ = _viewModel.InitializeAsync();
        _ = RevealAfterAsync(splash, Reveal);
    }

    private static async Task RevealAfterAsync(Window splash, Action reveal)
    {
        await Task.Delay(TimeSpan.FromSeconds(3.2));
        if (splash.Dispatcher.HasShutdownStarted)
            return;
        splash.Dispatcher.Invoke(reveal);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs args)
    {
        MessageBox.Show(
            args.Exception.Message,
            "SimTrade",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        args.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _viewModel?.Dispose();
        try
        {
            _instanceMutex?.ReleaseMutex();
        }
        catch
        {
        }
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
