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
            ?? throw new InvalidOperationException("Relay vrátil prázdnou odpověď na vytvoření pozvánky.");
        return (result.Code, result.ExpiresAtUtc);
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

    public async Task<IReadOnlyList<RegisteredDevice>> GetRegisteredDevicesAsync(Uri endpoint, string adminSecret, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(adminSecret);

        var listUri = new Uri(ToHttpUri(endpoint), "admin/devices");
        using var request = new HttpRequestMessage(HttpMethod.Get, listUri);
        request.Headers.Add("X-Admin-Secret", adminSecret);

        using var response = await _httpClient.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("Relay odmítl zadané admin heslo.");
        response.EnsureSuccessStatusCode();

        var results = await response.Content.ReadFromJsonAsync<List<RegisteredDeviceSummary>>(HttpJsonOptions, ct) ?? [];
        return results.Select(d => new RegisteredDevice(d.Id, d.DisplayName, d.CreatedAtUtc, d.DirectoryDisplayName, d.LastActiveAtUtc, d.PendingOutboxCount)).ToList();
    }

    public async Task DeregisterDeviceAsync(Uri endpoint, string adminSecret, Guid deviceId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(adminSecret);

        var deregisterUri = new Uri(ToHttpUri(endpoint), $"admin/devices/{deviceId}/deregister");
        using var request = new HttpRequestMessage(HttpMethod.Post, deregisterUri);
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

    public async Task<string> CreateWireGuardClientAsync(Uri endpoint, string adminSecret, string memberName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(adminSecret);
        ArgumentException.ThrowIfNullOrWhiteSpace(memberName);

        var wireguardUri = new Uri(ToHttpUri(endpoint), "admin/wireguard/clients");
        using var request = new HttpRequestMessage(HttpMethod.Post, wireguardUri)
        {
            Content = JsonContent.Create(new { Name = memberName }, options: HttpJsonOptions)
        };
        request.Headers.Add("X-Admin-Secret", adminSecret);

        using var response = await _httpClient.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("Relay odmítl zadané admin heslo.");
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException("Přidávání WireGuard přístupu není na tomto relay serveru nastavené.");
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<WireGuardClientResult>(HttpJsonOptions, ct)
            ?? throw new InvalidOperationException("Relay vrátil prázdnou odpověď na vytvoření WireGuard přístupu.");
        return result.ConfigurationText;
    }

    private sealed record WireGuardClientResult(string ConfigurationText);

    private sealed record InviteResponse(string Code, DateTimeOffset ExpiresAtUtc);

    /// <summary>Mirrors the relay's own <c>SecureApp.Relay.Contracts.RegisteredDeviceSummary</c> (2.1, 2026-09-17) — duplicated rather than shared, since this Presentation-layer client has no project reference to the Relay's own assembly (same reasoning as <see cref="InviteResponse"/> already established for the invite-code response shape).</summary>
    private sealed record RegisteredDeviceSummary(Guid Id, string DisplayName, DateTimeOffset CreatedAtUtc, string? DirectoryDisplayName, DateTimeOffset? LastActiveAtUtc, int PendingOutboxCount);
}
