namespace SecureApp.Domain.Interfaces.Services;

/// <summary>
/// Vestavěný WireGuard tunel (2026-10-31, VPN_EMBED_PLAN.md): appka si sama od relay vyžádá config svého
/// tunelu (stejný formát jako .conf). Poslední platný config si pamatuje v zabezpečeném úložišti, protože
/// relay je dosažitelný jen přes tunel — po restartu telefonu by jinak tunel nikdy nenaběhl.
/// </summary>
public interface IWireGuardTunnelConfigService
{
    /// <summary>
    /// Config z relay (a uloží ho do cache). Null = relay pro zařízení tunel nemá / byl zrušen (cache se smaže,
    /// tunel se má vypnout). Když relay není dosažitelný, vrátí poslední uložený config.
    /// </summary>
    Task<string?> FetchConfigAsync(CancellationToken ct = default);

    /// <summary>Poslední config uložený na zařízení (bez sítě) — pro start tunelu hned po restartu.</summary>
    Task<string?> GetCachedConfigAsync(CancellationToken ct = default);
}
