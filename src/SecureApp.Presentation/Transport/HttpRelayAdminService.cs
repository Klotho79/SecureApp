using System.Net;
using System.Net.Http.Json;
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

    private readonly HttpClient _httpClient = new();

    // Deliberately no ISecureVaultKeyStore dependency (2026-09-07) — see IRelayAdminService's own
    // remarks: the admin secret is never persisted by this class at all, only ever passed straight
    // through to the one HTTP call that needs it.

    public async Task<(string InviteCode, DateTimeOffset ExpiresAtUtc)> CreateInviteAsync(Uri endpoint, string adminSecret, string? displayNameHint, int validForMinutes, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(adminSecret);

        var invitesUri = new Uri(ToHttpUri(endpoint), "admin/invites");
        using var request = new HttpRequestMessage(HttpMethod.Post, invitesUri)
        {
            Content = JsonContent.Create(new { DisplayNameHint = displayNameHint, ValidForMinutes = validForMinutes }, options: HttpJsonOptions)
        };
        request.Headers.Add("X-Admin-Secret", adminSecret);

        using var response = await _httpClient.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("Relay odmítl zadané admin heslo.");
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<InviteResponse>(HttpJsonOptions, ct)
            ?? throw new InvalidOperationException("Relay returned an empty invite response.");
        return (result.Code, result.ExpiresAtUtc);
    }

    public async Task<IReadOnlyList<PendingActivationRequest>> GetPendingActivationRequestsAsync(Uri endpoint, string adminSecret, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(adminSecret);

        var listUri = new Uri(ToHttpUri(endpoint), "admin/activation-requests");
        using var request = new HttpRequestMessage(HttpMethod.Get, listUri);
        request.Headers.Add("X-Admin-Secret", adminSecret);

        using var response = await _httpClient.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("Relay odmítl zadané admin heslo.");
        response.EnsureSuccessStatusCode();

        var results = await response.Content.ReadFromJsonAsync<List<ActivationRequestSummary>>(HttpJsonOptions, ct) ?? [];
        return results.Select(r => new PendingActivationRequest(r.Id, r.DisplayName, r.Email, r.KeyFingerprint, r.CreatedAtUtc)).ToList();
    }

    public Task ApproveActivationRequestAsync(Uri endpoint, string adminSecret, Guid requestId, CancellationToken ct = default)
        => PostActivationDecisionAsync(endpoint, adminSecret, requestId, "approve", ct);

    public Task RejectActivationRequestAsync(Uri endpoint, string adminSecret, Guid requestId, CancellationToken ct = default)
        => PostActivationDecisionAsync(endpoint, adminSecret, requestId, "reject", ct);

    private async Task PostActivationDecisionAsync(Uri endpoint, string adminSecret, Guid requestId, string action, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(adminSecret);

        var decisionUri = new Uri(ToHttpUri(endpoint), $"admin/activation-requests/{requestId}/{action}");
        using var request = new HttpRequestMessage(HttpMethod.Post, decisionUri);
        request.Headers.Add("X-Admin-Secret", adminSecret);

        using var response = await _httpClient.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("Relay odmítl zadané admin heslo.");
        if (response.StatusCode == HttpStatusCode.Conflict)
            throw new InvalidOperationException("This request was already approved or rejected — nothing left to do.");
        response.EnsureSuccessStatusCode();
    }

    public async Task RequestDeployAsync(Uri endpoint, string adminSecret, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(adminSecret);

        var deployUri = new Uri(ToHttpUri(endpoint), "admin/deploy");
        using var request = new HttpRequestMessage(HttpMethod.Post, deployUri);
        request.Headers.Add("X-Admin-Secret", adminSecret);

        using var response = await _httpClient.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("Relay odmítl zadané admin heslo.");
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
