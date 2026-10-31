using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;

namespace SecureApp.Presentation.Transport;

/// <inheritdoc cref="IWireGuardTunnelConfigService"/>
public sealed class HttpWireGuardTunnelConfigService : IWireGuardTunnelConfigService
{
    private static readonly JsonSerializerOptions HttpJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ITransportSettingsRepository _transportSettingsRepository;
    private readonly ISecureVaultKeyStore _vault;
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(20) };

    public HttpWireGuardTunnelConfigService(ITransportSettingsRepository transportSettingsRepository, ISecureVaultKeyStore vault)
    {
        _transportSettingsRepository = transportSettingsRepository ?? throw new ArgumentNullException(nameof(transportSettingsRepository));
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
    }

    public async Task<string?> FetchConfigAsync(CancellationToken ct = default)
    {
        var configuration = await _transportSettingsRepository.GetAsync(ct);
        var deviceId = configuration?.AssignedDeviceId;
        var wsEndpoint = configuration?.EndpointUri;
        if (deviceId is null || wsEndpoint is null)
            return null; // appka ještě není registrovaná

        var secretBytes = await _vault.RetrieveSecretAsync(RelayDeviceVaultKeys.DeviceSecret, ct);
        if (secretBytes is null)
            return null;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(RelayHttp.ToHttpUri(wsEndpoint), "device/wireguard/config"));
            request.Headers.Add("X-Device-Id", deviceId.Value.ToString());
            request.Headers.Add("X-Device-Secret", Encoding.UTF8.GetString(secretBytes));

            using var response = await _httpClient.SendAsync(request, ct);
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized)
            {
                await _vault.RemoveSecretAsync(CacheVaultKey, ct);
                return null; // relay tunel nenakonfigurovaný, nebo byl zařízení zrušen - vestavěný tunel se má vypnout, ne si pamatovat starý config
            }
            response.EnsureSuccessStatusCode();

            var dto = await response.Content.ReadFromJsonAsync<ConfigDto>(HttpJsonOptions, ct);
            if (!string.IsNullOrWhiteSpace(dto?.ConfigurationText))
                await _vault.StoreSecretAsync(CacheVaultKey, Encoding.UTF8.GetBytes(dto.ConfigurationText), ct);
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
        var bytes = await _vault.RetrieveSecretAsync(CacheVaultKey, ct);
        return bytes is null ? null : Encoding.UTF8.GetString(bytes);
    }

    private const string CacheVaultKey = "wireguard.tunnel.config";

    private sealed record ConfigDto(string? ConfigurationText);
}
