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
/// <see cref="UpAsync"/>/<see cref="DownAsync"/> si samy odloží blokující GoBackend.SetState na
/// thread-pool vlákno (viz jejich komentáře — volání přímo na hlavním vlákně způsobí deadlock).
/// VPN souhlas systému (<see cref="PrepareIntent"/>) se appce zobrazí jen jednou.
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

    private volatile bool _isUp;
    private volatile string? _appliedConfig;

    /// <summary>True když tunel běží přesně s tímhle configem — volající pak nemusí SetState(Up) opakovat (zbytečně by tunel překonfiguroval).</summary>
    public bool IsUpWith(string configText) => _isUp && _appliedConfig == configText;

    public void OnStateChange(ITunnel.State? state)
    {
        _isUp = state == ITunnel.State.Up;
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

    /// <summary>
    /// Spustí/aktualizuje tunel z config textu, jak ho appka dostala z relay (stejný formát jako .conf,
    /// který dnes stahuje člověk). <see cref="GoBackend.SetState"/> uvnitř čeká na vlastní VpnService
    /// callback, který doběhne jen na hlavním vlákně (Looper) — proto tahle metoda jen zadá blokující
    /// práci na thread-pool vlákno a sama na výsledek čeká asynchronně (ne <c>.Wait()</c>/<c>.Result</c>),
    /// aby hlavní vlákno zůstalo volné zpracovat ten callback. Bez tohohle (volání přímo na hlavním
    /// vlákně) vznikne deadlock, který GoBackend po chvíli shodí jako TimeoutException (nalezeno
    /// 2026-10-31 v device_app_logs pro S23+ pilot).
    /// </summary>
    public async Task<bool> UpAsync(string configText)
    {
        try
        {
            await Task.Run(() =>
            {
                _backend ??= new GoBackend(_context);
                using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(configText));
                var config = Config.Parse(stream);
                _backend.SetState(this, ITunnel.State.Up!, config);
            }).ConfigureAwait(true);
            _appliedConfig = configText;
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error(nameof(WireGuardTunnelManager), "tunnel up failed", ex);
            WireGuardTunnelStatus.Set($"Tunel: chyba ({ex.Message})");
            return false;
        }
    }

    public async Task DownAsync()
    {
        try
        {
            await Task.Run(() => _backend?.SetState(this, ITunnel.State.Down!, null)).ConfigureAwait(true);
            _appliedConfig = null;
        }
        catch (Exception ex)
        {
            AppLog.Error(nameof(WireGuardTunnelManager), "tunnel down failed", ex);
        }
    }
}
