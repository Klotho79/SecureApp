using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;

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

    public async Task<IReadOnlyList<PendingActivationRequest>> GetPendingActivationRequestsAsync(Uri endpoint, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        var adminSecret = await GetStoredAdminSecretAsync(ct);
        var listUri = new Uri(ToHttpUri(endpoint), "admin/activation-requests");
        using var request = new HttpRequestMessage(HttpMethod.Get, listUri);
        request.Headers.Add("X-Admin-Secret", adminSecret);

        using var response = await _httpClient.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("The relay rejected the stored admin secret — it may be wrong or have changed.");
        response.EnsureSuccessStatusCode();

        var results = await response.Content.ReadFromJsonAsync<List<ActivationRequestSummary>>(HttpJsonOptions, ct) ?? [];
        return results.Select(r => new PendingActivationRequest(r.Id, r.DisplayName, r.Email, r.KeyFingerprint, r.CreatedAtUtc)).ToList();
    }

    public Task ApproveActivationRequestAsync(Uri endpoint, Guid requestId, CancellationToken ct = default)
        => PostActivationDecisionAsync(endpoint, requestId, "approve", ct);

    public Task RejectActivationRequestAsync(Uri endpoint, Guid requestId, CancellationToken ct = default)
        => PostActivationDecisionAsync(endpoint, requestId, "reject", ct);

    private async Task PostActivationDecisionAsync(Uri endpoint, Guid requestId, string action, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        var adminSecret = await GetStoredAdminSecretAsync(ct);
        var decisionUri = new Uri(ToHttpUri(endpoint), $"admin/activation-requests/{requestId}/{action}");
        using var request = new HttpRequestMessage(HttpMethod.Post, decisionUri);
        request.Headers.Add("X-Admin-Secret", adminSecret);

        using var response = await _httpClient.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("The relay rejected the stored admin secret — it may be wrong or have changed.");
        if (response.StatusCode == HttpStatusCode.Conflict)
            throw new InvalidOperationException("This request was already approved or rejected — nothing left to do.");
        response.EnsureSuccessStatusCode();
    }

    private async Task<string> GetStoredAdminSecretAsync(CancellationToken ct)
    {
        var secretBytes = await _vault.RetrieveSecretAsync(RelayDeviceVaultKeys.AdminSecret, ct)
            ?? throw new InvalidOperationException("No admin secret is stored on this device yet — enter it above first.");
        return Encoding.UTF8.GetString(secretBytes);
    }

    public async Task RequestDeployAsync(Uri endpoint, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        var secretBytes = await _vault.RetrieveSecretAsync(RelayDeviceVaultKeys.AdminSecret, ct)
            ?? throw new InvalidOperationException("No admin secret is stored on this device yet — enter it above first.");
        var adminSecret = Encoding.UTF8.GetString(secretBytes);

        var deployUri = new Uri(ToHttpUri(endpoint), "admin/deploy");
        using var request = new HttpRequestMessage(HttpMethod.Post, deployUri);
        request.Headers.Add("X-Admin-Secret", adminSecret);

        using var response = await _httpClient.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("The relay rejected the stored admin secret — it may be wrong or have changed.");
        response.EnsureSuccessStatusCode();
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

    /// <summary>Mirrors the relay's own <c>SecureApp.Relay.Contracts.ActivationRequestSummary</c> — duplicated rather than shared, since this Presentation-layer client has no project reference to the Relay's own assembly (same reasoning as <see cref="InviteResponse"/> already established for the invite-code response shape).</summary>
    private sealed record ActivationRequestSummary(Guid Id, string DisplayName, string Email, string KeyFingerprint, DateTimeOffset CreatedAtUtc);
}
