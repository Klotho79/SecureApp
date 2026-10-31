using CommunityToolkit.Mvvm.ComponentModel;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// Vestavěný tunel (2026-10-31, VPN_EMBED_PLAN.md) — jen status, žádné nastavování (to je celý
/// smysl: appka tunel sám nakonfiguruje a admin parametry upravuje přes relay, ne uživatel tady).
/// Jen Android ho má zapojený (<see cref="Infrastructure.WireGuardTunnelStatus"/> zůstává null
/// jinde), proto <see cref="IsTunnelSupported"/> stejným vzorem jako <c>IsBackgroundRunSupported</c>.
/// </summary>
public sealed partial class SettingsViewModel
{
    public bool IsTunnelSupported => DeviceInfo.Current.Platform == DevicePlatform.Android;

    [ObservableProperty]
    public partial string? TunnelStatusText { get; set; }

    public bool HasTunnelStatus => !string.IsNullOrEmpty(TunnelStatusText);

    partial void OnTunnelStatusTextChanged(string? value) => OnPropertyChanged(nameof(HasTunnelStatus));

    public void RefreshTunnelStatus() => TunnelStatusText = Infrastructure.WireGuardTunnelStatus.Text;
}
