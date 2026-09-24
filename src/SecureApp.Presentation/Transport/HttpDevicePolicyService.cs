using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;

namespace SecureApp.Presentation.Transport;

/// <inheritdoc cref="IDevicePolicyService"/>
public sealed class HttpDevicePolicyService : IDevicePolicyService
{
    private static readonly JsonSerializerOptions HttpJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ITransportSettingsRepository _transportSettingsRepository;
    private readonly ISecureVaultKeyStore _vault;
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(20) };

    public HttpDevicePolicyService(ITransportSettingsRepository transportSettingsRepository, ISecureVaultKeyStore vault)
    {
        _transportSettingsRepository = transportSettingsRepository ?? throw new ArgumentNullException(nameof(transportSettingsRepository));
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
    }

    public async Task<DevicePolicy> GetMyPolicyAsync(CancellationToken ct = default)
    {
        var configuration = await _transportSettingsRepository.GetAsync(ct);
        var deviceId = configuration?.AssignedDeviceId;
        var wsEndpoint = configuration?.EndpointUri;
        if (deviceId is null || wsEndpoint is null)
            return DevicePolicy.Unmanaged; // not registered yet — nothing to apply

        var secretBytes = await _vault.RetrieveSecretAsync(RelayDeviceVaultKeys.DeviceSecret, ct);
        if (secretBytes is null)
            return DevicePolicy.Unmanaged;

        var uri = new Uri(ToHttpUri(wsEndpoint), "me/policy");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Add("X-Device-Id", deviceId.Value.ToString());
        request.Headers.Add("X-Device-Secret", Encoding.UTF8.GetString(secretBytes));

        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<PolicyDto>(HttpJsonOptions, ct);
        if (dto is null)
            return DevicePolicy.Unmanaged;

        var role = dto.Role is null ? (Role?)null : (Role)dto.Role.Value;
        return new DevicePolicy(role, dto.HiddenTabs ?? []);
    }

    private static Uri ToHttpUri(Uri wsEndpoint)
    {
        var scheme = wsEndpoint.Scheme switch { "ws" => "http", "wss" => "https", _ => wsEndpoint.Scheme };
        return new UriBuilder(wsEndpoint) { Scheme = scheme, Port = wsEndpoint.Port }.Uri;
    }

    private sealed record PolicyDto(int? Role, List<string>? HiddenTabs);
}
