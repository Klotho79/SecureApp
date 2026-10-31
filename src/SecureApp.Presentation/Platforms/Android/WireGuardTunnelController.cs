using Android.Content;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Presentation.Infrastructure;

namespace SecureApp.Presentation.Platforms.Android;

/// <inheritdoc cref="IWireGuardTunnelController"/>
public sealed class WireGuardTunnelController : IWireGuardTunnelController
{
    private readonly IWireGuardTunnelConfigService _configService;
    private readonly WireGuardTunnelManager _tunnel;

    public WireGuardTunnelController(IWireGuardTunnelConfigService configService)
    {
        _configService = configService;
        _tunnel = new WireGuardTunnelManager(global::Android.App.Application.Context);
    }

    /// <summary>Android-specific hook for <see cref="MainActivity"/> — null = appka už souhlas má.</summary>
    public Intent? PrepareIntent => _tunnel.PrepareIntent;

    public async Task SyncAsync(CancellationToken ct = default)
    {
        try
        {
            // Fetch napřed (2026-10-31) - zařízení, co relay vůbec nemá v pilotu (viz
            // HasPendingConfigAsync), se tímhle poznají a nic se jim nezapíná ani nepřepisuje stav.
            var configText = await _configService.FetchConfigAsync(ct);
            if (configText is null)
            {
                await _tunnel.DownAsync();
                return;
            }

            if (_tunnel.PrepareIntent is not null)
            {
                WireGuardTunnelStatus.Set("Tunel: čeká na povolení VPN");
                return; // background job nemůže zobrazit systémový dialog - to řeší MainActivity
            }

            await _tunnel.UpAsync(configText);
        }
        catch (Exception ex)
        {
            AppLog.Error(nameof(WireGuardTunnelController), "tunnel sync failed", ex);
            WireGuardTunnelStatus.Set($"Tunel: chyba ({ex.Message})");
        }
    }

    public async Task<bool> HasPendingConfigAsync(CancellationToken ct = default)
    {
        try
        {
            return await _configService.FetchConfigAsync(ct) is not null;
        }
        catch (Exception ex)
        {
            AppLog.Error(nameof(WireGuardTunnelController), "checking for a pending tunnel config failed", ex);
            return false;
        }
    }
}
