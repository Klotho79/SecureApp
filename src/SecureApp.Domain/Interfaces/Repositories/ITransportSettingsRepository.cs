using SecureApp.Domain.Entities;

namespace SecureApp.Domain.Interfaces.Repositories;

/// <summary>
/// Persists the single stored <see cref="TransportEndpointConfiguration"/> — mirrors
/// <c>IUserRepository</c>'s single-row model, so the app can read a default endpoint at startup
/// for automatic connect while still letting it be changed manually at any time.
/// </summary>
public interface ITransportSettingsRepository
{
    /// <summary>Null if no endpoint has ever been configured on this device.</summary>
    Task<TransportEndpointConfiguration?> GetAsync(CancellationToken ct = default);

    /// <summary>Replaces whatever configuration exists (there is only ever one) with <paramref name="configuration"/>.</summary>
    Task SaveAsync(TransportEndpointConfiguration configuration, CancellationToken ct = default);
}
