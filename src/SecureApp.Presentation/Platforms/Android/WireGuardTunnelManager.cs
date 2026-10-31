using Android.Content;
using Android.Net;
using Com.Wireguard.Android.Backend;
using Com.Wireguard.Config;
using SecureApp.Presentation.Infrastructure;

namespace SecureApp.Presentation.Platforms.Android;

/// <summary>
/// Vestavěný WireGuard tunel (2026-10-31, VPN_EMBED_PLAN.md) — appka tunel sama vytváří a udržuje,
/// uživatel neinstaluje žádnou cizí appku ani nic nenastavuje. <see cref="GoBackend"/> je
/// z com.wireguard.android:tunnel (binding SecureApp.WireGuard.Android, ověřeno feasibility
/// testem 2026-10-31). Config text appka dostává z relay stejně jako dnes (admin endpoint
/// s TuneWireGuardClientConfig), jen ho teď appka sama spotřebuje přes <see cref="Config.Parse(Stream?)"/>
/// místo člověka, co ho importoval do cizí appky.
///
/// Zatím jen plumbing — nic appku nevolá (žádné napojení na Nastavení / background job / data
/// z relay). <see cref="Up"/> musí volající spustit mimo hlavní vlákno (GoBackend.SetState je
/// blokující). VPN souhlas systému (<see cref="PrepareIntent"/>) se appce zobrazí jen jednou.
/// </summary>
public sealed class WireGuardTunnelManager : Java.Lang.Object, ITunnel
{
    private const string TunnelName = "secureapp";

    private readonly Context _context;
    private GoBackend? _backend;

    public WireGuardTunnelManager(Context context)
    {
        _context = context;
    }

    public string? Name => TunnelName;

    public void OnStateChange(ITunnel.State? state)
    {
        AppLog.Event($"wireguard.state={state}");
        WireGuardTunnelStatus.Set(state == ITunnel.State.Up ? "Tunel: aktivní"
            : state == ITunnel.State.Down ? "Tunel: vypnutý"
            : "Tunel: přepíná se…");
    }

    /// <summary>
    /// Androidí jednorázový souhlas s VPN. Null = appka už souhlas má. Jinak appka musí tenhle
    /// Intent spustit přes StartActivityForResult (systémový dialog "SecureApp chce vytvořit
    /// VPN připojení") dřív, než se zavolá <see cref="Up"/>.
    /// </summary>
    public Intent? PrepareIntent => VpnService.Prepare(_context);

    /// <summary>Spustí/aktualizuje tunel z config textu, jak ho appka dostala z relay (stejný formát jako .conf, který dnes stahuje člověk).</summary>
    public bool Up(string configText)
    {
        try
        {
            _backend ??= new GoBackend(_context);
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(configText));
            var config = Config.Parse(stream);
            _backend.SetState(this, ITunnel.State.Up!, config);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error(nameof(WireGuardTunnelManager), "tunnel up failed", ex);
            WireGuardTunnelStatus.Set($"Tunel: chyba ({ex.Message})");
            return false;
        }
    }

    public void Down()
    {
        try
        {
            _backend?.SetState(this, ITunnel.State.Down!, null);
        }
        catch (Exception ex)
        {
            AppLog.Error(nameof(WireGuardTunnelManager), "tunnel down failed", ex);
        }
    }
}
