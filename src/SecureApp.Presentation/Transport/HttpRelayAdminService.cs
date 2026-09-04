using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SecureApp.Domain.Interfaces.Services;

namespace SecureApp.Presentation.Transport;

/// <inheritdoc cref="IRelayAdminService"/>
public sealed class HttpRelayAdminService : IRelayAdminService
{
    // Same reasoning as WebSocketMessageTransport.HttpJsonOptions — ASP.NET Core minimal APIs
    // serialize/bind camelCase by default, plain JsonSerializerOptions.Default is case-sensitive.
    private static readonly JsonSerializerOptions HttpJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISecureVaultKeyStore _vault;
    private readonly HttpClient _httpClient = new();

    public HttpRelayAdminService(ISecureVaultKeyStore vault)
    {
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
    }

    public async Task<bool> HasAdminSecretAsync(CancellationToken ct = default)
        => await _vault.RetrieveSecretAsync(RelayDeviceVaultKeys.AdminSecret, ct) is not null;

    public async Task SetAdminSecretAsync(string adminSecret, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adminSecret);
        await _vault.StoreSecretAsync(RelayDeviceVaultKeys.AdminSecret, Encoding.UTF8.GetBytes(adminSecret), ct);
    }

    public async Task<(string InviteCode, DateTimeOffset ExpiresAtUtc)> CreateInviteAsync(Uri endpoint, string? displayNameHint, int validForMinutes, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        var secretBytes = await _vault.RetrieveSecretAsync(RelayDeviceVaultKeys.AdminSecret, ct)
            ?? throw new InvalidOperationException("No admin secret is stored on this device yet — enter it above first.");
        var adminSecret = Encoding.UTF8.GetString(secretBytes);

        var invitesUri = new Uri(ToHttpUri(endpoint), "admin/invites");
        using var request = new HttpRequestMessage(HttpMethod.Post, invitesUri)
        {
            Content = JsonContent.Create(new { DisplayNameHint = displayNameHint, ValidForMinutes = validForMinutes }, options: HttpJsonOptions)
        };
        request.Headers.Add("X-Admin-Secret", adminSecret);

        using var response = await _httpClient.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("The relay rejected the stored admin secret — it may be wrong or have changed.");
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<InviteResponse>(HttpJsonOptions, ct)
            ?? throw new InvalidOperationException("Relay returned an empty invite response.");
        return (result.Code, result.ExpiresAtUtc);
    }

    // Mirrors WebSocketMessageTransport.ToHttpUri — the Settings UI stores/edits one ws:// address
    // for both the WebSocket connection and every HTTP admin/device call, so this needs the same
    // ws->http / wss->https rewrite.
    private static Uri ToHttpUri(Uri wsEndpoint)
    {
        var scheme = wsEndpoint.Scheme switch
        {
            "ws" => "http",
            "wss" => "https",
            _ => wsEndpoint.Scheme
        };
        return new UriBuilder(wsEndpoint) { Scheme = scheme, Port = wsEndpoint.Port }.Uri;
    }

    private sealed record InviteResponse(string Code, DateTimeOffset ExpiresAtUtc);
}
