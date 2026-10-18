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
        // Expired while the app stayed open — let the guard re-evaluate (chat pages only, 2026-10-18).
        if (!IsScreenCaptureAllowed)
            ScreenCaptureGuard.Refresh();
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
            ScreenCaptureGuard.Refresh(); // Settings itself is not a chat page, so this stays unblocked here
        }
        UpdateScreenCaptureStatusText();
    }

    private void UpdateScreenCaptureStatusText() =>
        ScreenCaptureStatusText = ScreenCaptureDebugOverride.AllowedUntilUtc is { } until
            ? $"Snímky obrazovky povoleny do {until.ToLocalTime():d.M. H:mm} — pak se ochrana zapne sama."
            : "Ochrana zapnutá jen v chatu — tam jsou snímky a nahrávání obrazovky blokované, jinde ne.";

    private static INativeDlpService? DlpService =>
        IPlatformApplication.Current?.Services.GetService<INativeDlpService>();
}
