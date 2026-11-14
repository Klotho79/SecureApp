using System.Net.Http.Json;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;

namespace SecureApp.Presentation.Transport;

/// <inheritdoc cref="IDevicePolicyService"/>
/// <remarks>Endpoint and device auth come from <see cref="RelayServiceBase"/>; a device that is not registered yet simply has no policy.</remarks>
public sealed class HttpDevicePolicyService(ITransportSettingsRepository transportSettingsRepository, ISecureVaultKeyStore vault)
    : RelayServiceBase(vault, transportSettingsRepository, TimeSpan.FromSeconds(20)), IDevicePolicyService
{
    public async Task<DevicePolicy> GetMyPolicyAsync(CancellationToken ct = default)
    {
        if (!await IsRegisteredAsync(ct))
            return DevicePolicy.Unmanaged; // not registered yet — nothing to apply

        var dto = await GetJsonAsync<PolicyDto>("me/policy", ct);
        if (dto is null)
            return DevicePolicy.Unmanaged;

        var role = dto.Role is null ? (Role?)null : (Role)dto.Role.Value;
        return new DevicePolicy(role, dto.HiddenTabs ?? [], dto.DocumentReviewer, dto.PiccTeam);
    }

    public async Task<bool> PublishMyEmailAsync(string email, CancellationToken ct = default)
    {
        if (!await IsRegisteredAsync(ct)) return false;
        await SendOrThrowAsync(HttpMethod.Post, "me/email", new { Email = email }, ct);
        return true;
    }

    /// <summary>True once the device has a relay address, an assigned id and its secret — before that, the policy calls quietly do nothing.</summary>
    private async Task<bool> IsRegisteredAsync(CancellationToken ct)
    {
        var configuration = await TransportSettings.GetAsync(ct);
        return configuration?.AssignedDeviceId is not null
               && configuration.EndpointUri is not null
               && await Vault.RetrieveSecretAsync(RelayDeviceVaultKeys.DeviceSecret, ct) is not null;
    }

    private sealed record PolicyDto(int? Role, List<string>? HiddenTabs, bool DocumentReviewer = false, bool PiccTeam = false);
}
