using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;

namespace SecureApp.Presentation.Transport;

/// <inheritdoc cref="IWireGuardTunnelConfigService"/>
/// <remarks>Endpoint and device auth come from <see cref="RelayServiceBase"/>.</remarks>
public sealed class HttpWireGuardTunnelConfigService(ITransportSettingsRepository transportSettingsRepository, ISecureVaultKeyStore vault)
    : RelayServiceBase(vault, transportSettingsRepository, TimeSpan.FromSeconds(20)), IWireGuardTunnelConfigService
{
    public async Task<string?> FetchConfigAsync(CancellationToken ct = default)
    {
        var configuration = await TransportSettings.GetAsync(ct);
        var deviceId = configuration?.AssignedDeviceId;
        var wsEndpoint = configuration?.EndpointUri;
        if (deviceId is null || wsEndpoint is null)
            return null; // appka ještě není registrovaná

        // Zařízení na veřejném wss:// tunel nepotřebuje — null tunel vypne (SyncAsync -> DownAsync) a uvolní VPN slot (FortiClient).
        if (RelayDefaults.IsPublicEndpoint(wsEndpoint))
            return null;

        if (await Vault.RetrieveSecretAsync(RelayDeviceVaultKeys.DeviceSecret, ct) is null)
            return null;

        try
        {
            using var request = await CreateRequestAsync(HttpMethod.Post, "device/wireguard/config", null, ct);
            using var response = await Http.SendAsync(request, ct);
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized)
            {
                await Vault.RemoveSecretAsync(CacheVaultKey, ct);
                return null; // relay tunel nenakonfigurovaný, nebo byl zařízení zrušen - vestavěný tunel se má vypnout, ne si pamatovat starý config
            }
            response.EnsureSuccessStatusCode();

            var dto = await response.Content.ReadFromJsonAsync<ConfigDto>(HttpJsonOptions, ct);
            if (!string.IsNullOrWhiteSpace(dto?.ConfigurationText))
                await Vault.StoreSecretAsync(CacheVaultKey, Encoding.UTF8.GetBytes(dto.ConfigurationText), ct);
            return dto?.ConfigurationText;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // Relay je dosažitelný jen přes tunel — když tunel neběží (po restartu), je tohle jediná cesta, jak ho zvednout.
            return await GetCachedConfigAsync(ct);
        }
    }

    public async Task<string?> GetCachedConfigAsync(CancellationToken ct = default)
    {
        var configuration = await TransportSettings.GetAsync(ct);
        if (RelayDefaults.IsPublicEndpoint(configuration?.EndpointUri))
            return null; // StartFromCache po restartu telefonu nesmí tunel znovu zvednout

        var bytes = await Vault.RetrieveSecretAsync(CacheVaultKey, ct);
        return bytes is null ? null : Encoding.UTF8.GetString(bytes);
    }

    private const string CacheVaultKey = "wireguard.tunnel.config";

    private sealed record ConfigDto(string? ConfigurationText);
}
