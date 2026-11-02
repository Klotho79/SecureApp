using CommunityToolkit.Mvvm.ComponentModel;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// Vestavěný tunel (2026-10-31, VPN_EMBED_PLAN.md) — jen status, žádné nastavování (to je celý
/// smysl: appka tunel sám nakonfiguruje a admin parametry upravuje přes relay, ne uživatel tady).
/// Android a Windows ho mají zapojený (<see cref="Infrastructure.WireGuardTunnelStatus"/> zůstává null
/// jinde), proto <see cref="IsTunnelSupported"/> stejným vzorem jako <c>IsBackgroundRunSupported</c>.
/// </summary>
public sealed partial class SettingsViewModel
{
    public bool IsTunnelSupported => DeviceInfo.Current.Platform == DevicePlatform.Android || DeviceInfo.Current.Platform == DevicePlatform.WinUI;

    [ObservableProperty]
    public partial string? TunnelStatusText { get; set; }

    public bool HasTunnelStatus => !string.IsNullOrEmpty(TunnelStatusText);

    partial void OnTunnelStatusTextChanged(string? value) => OnPropertyChanged(nameof(HasTunnelStatus));

    public void RefreshTunnelStatus()
    {
        TunnelStatusText = Infrastructure.WireGuardTunnelStatus.Text;
#if WINDOWS
        _ = RefreshWindowsTunnelStatusAsync();
#endif
    }

#if WINDOWS
    /// <summary>
    /// Stav služby se zjišťuje při každém otevření Nastavení (2026-11-02): zpráva z posledního běhu synchronizace žije jen v paměti
    /// a po startu appky je prázdná až do prvního běhu, takže karta Tunel neříkala nic. Zpráva o neúspěchu/odkladu se přidá jako
    /// vysvětlení, jen když tunel neběží.
    /// </summary>
    private async Task RefreshWindowsTunnelStatusAsync()
    {
        try
        {
            var live = await Infrastructure.WireGuardWindowsSetup.DescribeAsync();
            var last = Infrastructure.WireGuardTunnelStatus.Text;
            var running = live.StartsWith("Tunel: běží", StringComparison.Ordinal);
            var note = !running && !string.IsNullOrEmpty(last) && !last.StartsWith("Tunel: aktivní", StringComparison.Ordinal)
                ? $" ({last.Replace("Tunel: ", "")})"
                : "";
            TunnelStatusText = live + note;
        }
        catch (Exception ex)
        {
            TunnelStatusText = $"Tunel: stav se nepodařilo zjistit ({ex.Message})";
        }
    }
#endif
}
