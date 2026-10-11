using CommunityToolkit.Mvvm.ComponentModel;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Presentation.Infrastructure;

namespace SecureApp.Presentation.ViewModels;

/// <summary>Admin tab: temporarily allow screenshots on this device for debugging — see <see cref="ScreenCaptureDebugOverride"/>.</summary>
public sealed partial class SettingsViewModel
{
    [ObservableProperty]
    public partial bool IsScreenCaptureAllowed { get; set; }

    [ObservableProperty]
    public partial string? ScreenCaptureStatusText { get; set; }

    private bool _isLoadingScreenCaptureState;

    private void LoadScreenCaptureState()
    {
        _isLoadingScreenCaptureState = true;
        IsScreenCaptureAllowed = ScreenCaptureDebugOverride.IsActive;
        _isLoadingScreenCaptureState = false;
        // Expired while the app stayed open — put the block back now rather than waiting for a relaunch.
        if (!IsScreenCaptureAllowed)
            DlpService?.PreventScreenCapture();
        UpdateScreenCaptureStatusText();
    }

    partial void OnIsScreenCaptureAllowedChanged(bool value)
    {
        if (_isLoadingScreenCaptureState) return;

        if (value)
        {
            ScreenCaptureDebugOverride.Enable();
            DlpService?.AllowScreenCapture();
        }
        else
        {
            ScreenCaptureDebugOverride.Disable();
            DlpService?.PreventScreenCapture();
        }
        UpdateScreenCaptureStatusText();
    }

    private void UpdateScreenCaptureStatusText() =>
        ScreenCaptureStatusText = ScreenCaptureDebugOverride.AllowedUntilUtc is { } until
            ? $"Snímky obrazovky povoleny do {until.ToLocalTime():d.M. H:mm} — pak se ochrana zapne sama."
            : "Ochrana zapnutá — snímky a nahrávání obrazovky jsou blokované.";

    private static INativeDlpService? DlpService =>
        IPlatformApplication.Current?.Services.GetService<INativeDlpService>();
}
