namespace SecureApp.Domain.Interfaces.Services;

/// <summary>
/// Vestavěný tunel (2026-10-31, VPN_EMBED_PLAN.md) — platformní protějšek k
/// <see cref="IWireGuardTunnelConfigService"/>: stáhne aktuální config a podle něj tunel
/// zapne/aktualizuje/vypne. Jen Android má implementaci (registrováno podmíněně v MauiProgram.cs);
/// na platformách bez vestavěného tunelu (dnes Windows, fáze "Windows: vestavěný tunel" v plánu)
/// <c>GetService&lt;IWireGuardTunnelController&gt;()</c> vrátí null a volající to má přeskočit,
/// stejně jako ostatní jen-Android služby v App.xaml.cs.
/// </summary>
public interface IWireGuardTunnelController
{
    /// <summary>No-op, dokud appka nemá systémový VPN souhlas (ten vyřizuje MainActivity) — další tick to zkusí znovu.</summary>
    Task SyncAsync(CancellationToken ct = default);

    /// <summary>
    /// Zjistí (fetch z relay), jestli pro tohle zařízení existuje config, BEZ aby appka cokoliv
    /// zapínala — <see cref="SecureApp.Presentation.Platforms.Android.MainActivity"/> tohle volá
    /// dřív, než vůbec zobrazí systémový VPN dialog, aby ho zařízení bez configu (pilot - 2026-10-31,
    /// jen S23+, viz VPN_EMBED_PLAN.md) nikdy nevidělo.
    /// </summary>
    Task<bool> HasPendingConfigAsync(CancellationToken ct = default);
}
