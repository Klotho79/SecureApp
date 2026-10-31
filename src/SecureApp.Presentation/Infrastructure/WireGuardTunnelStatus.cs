namespace SecureApp.Presentation.Infrastructure;

/// <summary>
/// Vestavěný tunel (2026-10-31, VPN_EMBED_PLAN.md) — poslední známý stav appce přečte Nastavení
/// → Systém → Připojení. Zapisuje ho jen Android (<c>Platforms/Android/WireGuardTunnelManager</c>
/// a <c>WireGuardTunnelController</c>); na ostatních platformách zůstává <c>null</c>, takže se
/// karta v Nastavení vůbec nezobrazí (<see cref="SecureApp.Presentation.ViewModels.SettingsViewModel"/>
/// skrývá ji podle <c>DeviceInfo.Current.Platform</c>, stejně jako <c>BackgroundRun.IsSupported</c>).
/// Čte se jen při zobrazení Nastavení (ne živě přes event) — appka funguje na pozadí bez ohledu
/// na to, jestli je karta zrovna otevřená.
/// </summary>
public static class WireGuardTunnelStatus
{
    public static string? Text { get; private set; }

    public static void Set(string text) => Text = text;
}
