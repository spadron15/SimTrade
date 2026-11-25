using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SimTrade.Services;

namespace SimTrade.ViewModels;

public partial class MainViewModel
{
    private readonly AppUpdateService _updates = new();
    private AppUpdateInfo? _pendingUpdate;
    private int _updateBusy;

    [ObservableProperty] private bool _updateAvailable;
    [ObservableProperty] private string _updateBannerText = "";
    [ObservableProperty] private string _updateStatusText = "Las actualizaciones se buscan automáticamente al iniciar.";
    [ObservableProperty] private double _updateProgress;
    [ObservableProperty] private bool _showUpdateProgress;
    [ObservableProperty] private bool _canInstallUpdate;
    [ObservableProperty] private bool _isCheckingUpdate;

    public bool CanCheckUpdates => !IsCheckingUpdate;
    partial void OnIsCheckingUpdateChanged(bool value) => OnPropertyChanged(nameof(CanCheckUpdates));
    partial void OnUpdateAvailableChanged(bool value) => OnPropertyChanged(nameof(ShowUpdateBanner));
    public bool ShowUpdateBanner => UpdateAvailable && !string.IsNullOrWhiteSpace(UpdateBannerText);

    private async Task CheckForUpdatesOnStartupAsync()
    {
        try
        {
            var update = await _updates.CheckLatestAsync(_cts.Token);
            if (update is null)
                return;

            var skipped = _repository.GetSetting("SkipUpdateVersion");
            if (!string.IsNullOrWhiteSpace(skipped)
                && AppUpdateService.TryParseVersion(skipped, out var skippedVersion)
                && skippedVersion == update.Version)
            {
                return;
            }

            ApplyPendingUpdate(update, announce: true);

            var answer = MessageBox.Show(
                "Hay una nueva versión de SimTrade (" + update.Version + ").\n\n"
                + "Versión actual: " + AppUpdateService.CurrentVersion + "\n\n"
                + "¿Descargar e instalar ahora?\n\n"
                + "Sí = actualizar · No = más tarde · Cancelar = omitir esta versión",
                "Actualización disponible",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Information);

            if (answer == MessageBoxResult.Cancel)
            {
                _repository.SetSetting("SkipUpdateVersion", update.Version.ToString());
                UpdateStatusText = "Omitiste la versión " + update.Version + ". Puedes buscarla otra vez en Acerca de SimTrade.";
                return;
            }

            if (answer == MessageBoxResult.Yes)
                await DownloadAndInstallUpdateAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // Silencioso al arranque: no molestar si GitHub no responde.
        }
    }

    private void ApplyPendingUpdate(AppUpdateInfo update, bool announce)
    {
        _pendingUpdate = update;
        UpdateAvailable = true;
        CanInstallUpdate = true;
        UpdateBannerText = "Nueva versión " + update.Version + " disponible";
        UpdateStatusText = "Versión " + update.Version + " lista para descargar"
            + (update.SizeBytes > 0
                ? " (" + Math.Round(update.SizeBytes / (1024.0 * 1024.0), 1).ToString("0.0", MxFormat.Culture) + " MB)."
                : ".");
        if (announce)
            SetStatus("Hay una actualización de SimTrade (" + update.Version + ").", false);
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        if (Interlocked.Exchange(ref _updateBusy, 1) == 1)
            return;

        try
        {
            IsCheckingUpdate = true;
            CanInstallUpdate = false;
            ShowUpdateProgress = false;
            UpdateProgress = 0;
            UpdateStatusText = "Buscando actualizaciones en GitHub…";
            SetStatus(UpdateStatusText, false);

            var update = await _updates.CheckLatestAsync(_cts.Token);
            if (update is null)
            {
                _pendingUpdate = null;
                UpdateAvailable = false;
                UpdateBannerText = "";
                UpdateStatusText = "Ya tienes la última versión (" + AppUpdateService.CurrentVersion + ").";
                SetStatus(UpdateStatusText, false);
                return;
            }

            // Una búsqueda manual limpia el “omitir esta versión”.
            _repository.SetSetting("SkipUpdateVersion", "");
            ApplyPendingUpdate(update, announce: true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            UpdateStatusText = "No se pudo consultar actualizaciones. " + ex.Message;
            SetStatus(UpdateStatusText, true);
        }
        finally
        {
            IsCheckingUpdate = false;
            Interlocked.Exchange(ref _updateBusy, 0);
        }
    }

    [RelayCommand]
    private async Task InstallUpdateAsync() => await DownloadAndInstallUpdateAsync();

    private async Task DownloadAndInstallUpdateAsync()
    {
        if (_pendingUpdate is null)
        {
            await CheckForUpdatesAsync();
            if (_pendingUpdate is null)
                return;
        }

        if (Interlocked.Exchange(ref _updateBusy, 1) == 1)
            return;

        var update = _pendingUpdate!;
        try
        {
            CanInstallUpdate = false;
            ShowUpdateProgress = true;
            UpdateProgress = 0;
            UpdateStatusText = "Descargando SimTrade " + update.Version + "…";
            SetStatus(UpdateStatusText, false);

            var progress = new Progress<double>(value =>
            {
                UpdateProgress = value;
                UpdateStatusText = "Descargando SimTrade " + update.Version + "… "
                    + value.ToString("0", MxFormat.Culture) + "%";
            });

            var setupPath = await _updates.DownloadSetupAsync(update, progress, _cts.Token);
            UpdateStatusText = "Instalando actualización… SimTrade se cerrará un momento.";
            SetStatus(UpdateStatusText, false);
            AppUpdateService.LaunchInstallerAndExit(setupPath);
        }
        catch (OperationCanceledException)
        {
            UpdateStatusText = "Actualización cancelada.";
            CanInstallUpdate = true;
        }
        catch (Exception ex)
        {
            UpdateStatusText = "No se pudo descargar o instalar la actualización. " + ex.Message;
            SetStatus(UpdateStatusText, true);
            CanInstallUpdate = true;
            MessageBox.Show(UpdateStatusText, "Actualización", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            ShowUpdateProgress = false;
            Interlocked.Exchange(ref _updateBusy, 0);
        }
    }

    [RelayCommand]
    private void OpenReleasesPage()
    {
        var url = _pendingUpdate?.HtmlUrl;
        if (string.IsNullOrWhiteSpace(url))
            url = "https://github.com/" + AppUpdateService.GitHubOwner + "/" + AppUpdateService.GitHubRepo + "/releases";
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SetStatus("No se pudo abrir el navegador. " + ex.Message, true);
        }
    }

    [RelayCommand]
    private void DismissUpdateBanner()
    {
        UpdateBannerText = "";
        OnPropertyChanged(nameof(ShowUpdateBanner));
    }
}
