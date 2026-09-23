using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Presentation.Chat;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// Self-update (2026-09-23, user's own ask) — split into its own partial the same way
/// Opicentrum/WorkplaceColors already are, rather than growing the already-large main
/// SettingsViewModel.cs further. <see cref="IUpdateService"/> is cross-platform (registered on
/// every target), but <see cref="INativeAppInstaller"/> only exists on Android (see MauiProgram's
/// own remarks) — injected as nullable with a default so DI resolves it to null on every other
/// platform instead of throwing, and every command here checks it before use, degrading to "not
/// supported on this platform" rather than crashing.
/// </summary>
public sealed partial class SettingsViewModel
{
    [ObservableProperty]
    public partial string CurrentVersionText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? UpdateStatusText { get; set; }

    [ObservableProperty]
    public partial bool HasUpdateStatus { get; set; }

    [ObservableProperty]
    public partial bool IsUpdateAvailable { get; set; }

    [ObservableProperty]
    public partial bool IsCheckingForUpdate { get; set; }

    [ObservableProperty]
    public partial bool IsDownloadingUpdate { get; set; }

    [ObservableProperty]
    public partial double UpdateDownloadProgress { get; set; }

    /// <summary>Derived, not a value converter — same established pattern this app already uses throughout instead of IValueConverter classes.</summary>
    public bool CanCheckForUpdate => !IsCheckingForUpdate;
    public bool CanDownloadUpdate => !IsDownloadingUpdate;

    private string? _pendingUpdateLocalPath;

    partial void OnUpdateStatusTextChanged(string? value) => HasUpdateStatus = !string.IsNullOrEmpty(value);
    partial void OnIsCheckingForUpdateChanged(bool value) => OnPropertyChanged(nameof(CanCheckForUpdate));
    partial void OnIsDownloadingUpdateChanged(bool value) => OnPropertyChanged(nameof(CanDownloadUpdate));

    private void InitializeUpdatesSection()
    {
        CurrentVersionText = $"{Microsoft.Maui.ApplicationModel.AppInfo.Current.VersionString} (build {Microsoft.Maui.ApplicationModel.AppInfo.Current.BuildString})";
    }

    /// <summary>Member onboarding QR (2026-09-23, user's own ask: "potřebuji jen předání třeba přes QR nebo odkaz na stažení") — same relay host as chat, just the plain HTTP <c>/download</c> landing page instead of the <c>ws://</c> endpoint, so a brand-new phone (no device credential, no admin secret — matches /download's own "anyone already on the network" exposure) scans/opens it and gets the same install walkthrough <c>SecureApp.Relay</c>'s DownloadPageHtml already renders.</summary>
    [ObservableProperty]
    public partial string DownloadShareUrl { get; set; } = string.Empty;

    [ObservableProperty]
    public partial Microsoft.Maui.Controls.ImageSource? DownloadShareQrImage { get; set; }

    partial void OnDownloadShareUrlChanged(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            DownloadShareQrImage = null;
            return;
        }

        try
        {
            var png = QrImageGenerator.GeneratePng(value);
            DownloadShareQrImage = Microsoft.Maui.Controls.ImageSource.FromStream(() => new MemoryStream(png));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"QR generation failed for download share: {ex}");
            DownloadShareQrImage = null;
        }
    }

    /// <summary>Called from LoadAsync right after RelayEndpointText is resolved — same ws://→http:// scheme swap UpdateService.ResolveHttpBaseAsync does, kept separate since this needs it synchronously for display rather than for an HttpClient BaseAddress.</summary>
    private void UpdateDownloadShareUrl(string relayEndpointOrDefault)
    {
        if (!Uri.TryCreate(relayEndpointOrDefault, UriKind.Absolute, out var endpoint))
        {
            DownloadShareUrl = string.Empty;
            return;
        }

        var scheme = endpoint.Scheme == "wss" ? "https" : "http";
        DownloadShareUrl = new UriBuilder(endpoint) { Scheme = scheme, Port = endpoint.Port, Path = "/download" }.Uri.ToString();
    }

    [RelayCommand]
    private async Task CheckForUpdateAsync()
    {
        IsCheckingForUpdate = true;
        IsUpdateAvailable = false;
        UpdateStatusText = null;
        try
        {
            var result = await _updateService.CheckForUpdateAsync();
            if (result.ErrorMessage is not null)
            {
                UpdateStatusText = result.ErrorMessage;
            }
            else if (result.IsUpdateAvailable)
            {
                IsUpdateAvailable = true;
                UpdateStatusText = $"K dispozici je nová verze {result.LatestVersionName} (build {result.LatestVersionCode}).";
            }
            else
            {
                UpdateStatusText = "Máte nejnovější verzi.";
            }
        }
        finally
        {
            IsCheckingForUpdate = false;
        }
    }

    [RelayCommand]
    private async Task DownloadAndInstallUpdateAsync()
    {
        if (_nativeAppInstaller is null)
        {
            UpdateStatusText = "Instalace aktualizace přímo z appky není na této platformě podporovaná — stáhněte si APK ručně z odkazu ke stažení.";
            return;
        }

        IsDownloadingUpdate = true;
        UpdateDownloadProgress = 0;
        UpdateStatusText = "Stahuji aktualizaci…";
        try
        {
            var progress = new Progress<double>(p => UpdateDownloadProgress = p);
            _pendingUpdateLocalPath = await _updateService.DownloadUpdateAsync(progress);
            UpdateStatusText = "Staženo — otevírá se instalace…";
            _nativeAppInstaller.InstallApk(_pendingUpdateLocalPath);
        }
        catch (Exception ex)
        {
            UpdateStatusText = $"Stažení aktualizace se nezdařilo: {ex.Message}";
        }
        finally
        {
            IsDownloadingUpdate = false;
        }
    }
}
