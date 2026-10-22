using System.Text;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;

namespace SecureApp.Presentation.Transport;

/// <summary>
/// The relay HTTP plumbing every Http*Service shares: turning the configured WebSocket endpoint into
/// its HTTP(S) base URI, and stamping a request with this device's relay credentials (X-Device-Id /
/// X-Device-Secret). Previously copy-pasted into ten services; each service still keeps its own
/// one-line private wrappers, so call sites read the same.
/// </summary>
internal static class RelayHttp
{
    /// <summary>The Settings UI stores one ws:// address used for both the WebSocket connection and every HTTP call, so HTTP callers need the same ws->http / wss->https rewrite.</summary>
    public static Uri ToHttpUri(Uri wsEndpoint)
    {
        var scheme = wsEndpoint.Scheme switch
        {
            "ws" => "http",
            "wss" => "https",
            _ => wsEndpoint.Scheme
        };
        return new UriBuilder(wsEndpoint) { Scheme = scheme, Port = wsEndpoint.Port }.Uri;
    }

    public static async Task<Uri> GetHttpEndpointAsync(ITransportSettingsRepository transportSettingsRepository, CancellationToken ct)
    {
        var configuration = await transportSettingsRepository.GetAsync(ct);
        var wsEndpoint = configuration?.EndpointUri
            ?? throw new InvalidOperationException("Zatím není nastavená adresa relay serveru — nastavte ji nejprve v Nastavení.");
        return ToHttpUri(wsEndpoint);
    }

    /// <summary>Builds an authenticated request for <paramref name="path"/> relative to the relay's HTTP base — the "endpoint + device auth" pair every CMS-style service needs in one call.</summary>
    public static async Task<HttpRequestMessage> CreateAuthorizedRequestAsync(HttpMethod method, string path, ITransportSettingsRepository transportSettingsRepository, ISecureVaultKeyStore vault, CancellationToken ct)
    {
        var endpoint = await GetHttpEndpointAsync(transportSettingsRepository, ct);
        var request = new HttpRequestMessage(method, new Uri(endpoint, path));
        try
        {
            await AddDeviceAuthAsync(request, transportSettingsRepository, vault, ct);
        }
        catch
        {
            request.Dispose();
            throw;
        }
        return request;
    }

    public static async Task AddDeviceAuthAsync(HttpRequestMessage request, ITransportSettingsRepository transportSettingsRepository, ISecureVaultKeyStore vault, CancellationToken ct)
    {
        var configuration = await transportSettingsRepository.GetAsync(ct);
        var deviceId = configuration?.AssignedDeviceId
            ?? throw new InvalidOperationException("Nejprve se zaregistrujte u relay serveru v Nastavení.");
        var secretBytes = await vault.RetrieveSecretAsync(RelayDeviceVaultKeys.DeviceSecret, ct)
            ?? throw new InvalidOperationException("V úložišti chybí tajný klíč zařízení pro relay.");

        request.Headers.Add("X-Device-Id", deviceId.ToString());
        request.Headers.Add("X-Device-Secret", Encoding.UTF8.GetString(secretBytes));
    }
}
