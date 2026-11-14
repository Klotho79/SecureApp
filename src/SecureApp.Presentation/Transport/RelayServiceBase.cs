using System.Net.Http.Json;
using System.Text.Json;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;

namespace SecureApp.Presentation.Transport;

/// <summary>
/// Shared plumbing for the Http*Service classes that talk JSON to the relay with this device's credentials (2026-11-14 —
/// every one of them used to carry its own copy of the endpoint lookup, device-auth headers, send and status handling).
/// <see cref="GetJsonAsync{T}"/> throws on any failure, like the services' reads always did; <see cref="TrySendAsync"/> never
/// throws and only reports success, like their best-effort writes.
/// </summary>
public abstract class RelayServiceBase
{
    protected static readonly JsonSerializerOptions HttpJsonOptions = new(JsonSerializerDefaults.Web);

    protected ISecureVaultKeyStore Vault { get; }
    protected ITransportSettingsRepository TransportSettings { get; }
    protected HttpClient Http { get; }

    protected RelayServiceBase(ISecureVaultKeyStore vault, ITransportSettingsRepository transportSettingsRepository, TimeSpan? timeout = null)
    {
        Vault = vault ?? throw new ArgumentNullException(nameof(vault));
        TransportSettings = transportSettingsRepository ?? throw new ArgumentNullException(nameof(transportSettingsRepository));
        Http = RelayHttp.CreateClient(timeout);
    }

    /// <summary>An authenticated request for <paramref name="path"/> on the relay, with <paramref name="jsonBody"/> as its JSON content when given.</summary>
    protected async Task<HttpRequestMessage> CreateRequestAsync(HttpMethod method, string path, object? jsonBody, CancellationToken ct)
    {
        var request = await RelayHttp.CreateAuthorizedRequestAsync(method, path, TransportSettings, Vault, ct);
        if (jsonBody is not null)
            request.Content = JsonContent.Create(jsonBody, jsonBody.GetType(), options: HttpJsonOptions);
        return request;
    }

    /// <summary>
    /// GETs <paramref name="path"/> and reads the JSON body. Throws on a network error or a non-success status, except a 404
    /// when <paramref name="notFoundAsDefault"/> is set (an older relay without that route), which returns default.
    /// </summary>
    protected async Task<T?> GetJsonAsync<T>(string path, CancellationToken ct, bool notFoundAsDefault = false)
    {
        using var request = await CreateRequestAsync(HttpMethod.Get, path, null, ct);
        using var response = await Http.SendAsync(request, ct);
        if (notFoundAsDefault && response.StatusCode == System.Net.HttpStatusCode.NotFound) return default;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(HttpJsonOptions, ct);
    }

    /// <summary>Sends a request. Throws on a network error or a non-success status.</summary>
    protected async Task SendOrThrowAsync(HttpMethod method, string path, object? jsonBody, CancellationToken ct)
    {
        using var request = await CreateRequestAsync(method, path, jsonBody, ct);
        using var response = await Http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Sends a request and reports whether the relay accepted it. Never throws.</summary>
    protected async Task<bool> TrySendAsync(HttpMethod method, string path, object? jsonBody, CancellationToken ct)
    {
        try
        {
            using var request = await CreateRequestAsync(method, path, jsonBody, ct);
            using var response = await Http.SendAsync(request, ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
