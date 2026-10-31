namespace SecureApp.Domain.Interfaces.Services;

/// <summary>
/// Vestavěný tunel (2026-10-31, VPN_EMBED_PLAN.md) — appka si sama vyžádá/aktualizuje svůj
/// WireGuard config z relay (POST /device/wireguard/config, authentizovaně vlastním device
/// tokenem, žádný admin secret ani pickup kód). Null = appka ještě není registrovaná, relay
/// tunel nemá nakonfigurovaný, nebo byl zařízení administrátorem zrušen — volající má vestavěný
/// tunel v tom případě vypnout, ne si pamatovat poslední známý config.
/// </summary>
public interface IWireGuardTunnelConfigService
{
    Task<string?> FetchConfigAsync(CancellationToken ct = default);
}
