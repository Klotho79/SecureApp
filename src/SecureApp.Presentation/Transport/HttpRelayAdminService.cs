using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SecureApp.Domain.Interfaces.Repositories;
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

    // Still deliberately no persisted admin secret (2026-09-07) — see IRelayAdminService's own
    // remarks: every WRITE call below keeps taking it as a plain parameter, supplied fresh by the
    // caller for that one call only. ISecureVaultKeyStore/ITransportSettingsRepository (2026-10-17)
    // are new — the two READ-only calls (GetManagedDevicesAsync/GetRegisteredDevicesAsync) no longer
    // need the admin secret at all, just this device's own already-stored device-auth credential
    // (same X-Device-Id/X-Device-Secret headers HttpRoomStatusService.AddDeviceAuthAsync uses), since
    // the relay now accepts an Admin-role device on that alone (see Program.IsAdminAuthorizedOrAdminDevice).
    private readonly ISecureVaultKeyStore _vault;
    private readonly ITransportSettingsRepository _transportSettingsRepository;

    public HttpRelayAdminService(ISecureVaultKeyStore vault, ITransportSettingsRepository transportSettingsRepository)
    {
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _transportSettingsRepository = transportSettingsRepository ?? throw new ArgumentNullException(nameof(transportSettingsRepository));
    }

    private Task AddDeviceAuthAsync(HttpRequestMessage request, CancellationToken ct) => RelayHttp.AddDeviceAuthAsync(request, _transportSettingsRepository, _vault, ct);

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

    public async Task<IReadOnlyList<RegisteredDevice>> GetRegisteredDevicesAsync(Uri endpoint, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        var listUri = new Uri(ToHttpUri(endpoint), "admin/devices");
        using var request = new HttpRequestMessage(HttpMethod.Get, listUri);
        await AddDeviceAuthAsync(request, ct);

        using var response = await _httpClient.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("Toto zařízení nemá roli Admin na relay serveru.");
        response.EnsureSuccessStatusCode();

        var results = await response.Content.ReadFromJsonAsync<List<RegisteredDeviceSummary>>(HttpJsonOptions, ct) ?? [];
        return results.Select(d => new RegisteredDevice(d.Id, d.DisplayName, d.CreatedAtUtc, d.DirectoryDisplayName, d.LastActiveAtUtc, d.PendingOutboxCount, d.Platform, d.ArimContactId, d.ArimContactName)).ToList();
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

    public async Task<IReadOnlyList<string>> GetDeviceAppLogAsync(Uri endpoint, string adminSecret, Guid deviceId, string kind, int limit = 300, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(adminSecret);

        var uri = new Uri(ToHttpUri(endpoint), $"admin/applog/{deviceId}?kind={Uri.EscapeDataString(kind)}&limit={limit}");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Add("X-Admin-Secret", adminSecret);

        using var response = await _httpClient.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("Relay odmítl zadané admin heslo.");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<string>>(HttpJsonOptions, ct) ?? [];
    }

    public async Task<IReadOnlyList<ManagedDevice>> GetManagedDevicesAsync(Uri endpoint, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        var uri = new Uri(ToHttpUri(endpoint), "admin/users");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        await AddDeviceAuthAsync(request, ct);

        using var response = await _httpClient.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("Toto zařízení nemá roli Admin na relay serveru.");
        response.EnsureSuccessStatusCode();

        var results = await response.Content.ReadFromJsonAsync<List<ManagedDeviceSummary>>(HttpJsonOptions, ct) ?? [];
        return results.Select(d => new ManagedDevice(
            d.DeviceId, d.DisplayName,
            d.Role is null ? null : (SecureApp.Domain.Enums.Role)d.Role.Value,
            d.HiddenTabs ?? [], d.LastSeenUtc, d.DocumentReviewer, d.AppVersion, d.PiccTeam, d.HasSharedLibraryKey,
            d.ArimContactId, d.ArimContactName, d.ArimContactEmail)).ToList();
    }

    public async Task SetDevicePolicyAsync(Uri endpoint, string adminSecret, Guid deviceId, SecureApp.Domain.Enums.Role? role, IReadOnlyList<string> hiddenTabs, bool? isDocumentReviewer = null, bool? isPiccTeam = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(adminSecret);

        var uri = new Uri(ToHttpUri(endpoint), $"admin/users/{deviceId}/policy");
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = JsonContent.Create(new { Role = role is null ? (int?)null : (int)role.Value, HiddenTabs = hiddenTabs, DocumentReviewer = isDocumentReviewer, PiccTeam = isPiccTeam }, options: HttpJsonOptions)
        };
        request.Headers.Add("X-Admin-Secret", adminSecret);

        using var response = await _httpClient.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("Relay odmítl zadané admin heslo.");
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<LibraryDocumentReviewEntry>> SearchLibraryDocumentAuditAsync(Uri endpoint, string adminSecret, string? query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(adminSecret);

        var uri = new Uri(ToHttpUri(endpoint), string.IsNullOrWhiteSpace(query)
            ? "admin/library-documents/audit"
            : $"admin/library-documents/audit?query={Uri.EscapeDataString(query)}");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Add("X-Admin-Secret", adminSecret);

        using var response = await _httpClient.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("Relay odmítl zadané admin heslo.");
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<List<LibraryDocumentReviewEntry>>(HttpJsonOptions, ct) ?? [];
    }

    public async Task<IReadOnlyList<DocumentDownloadEntry>> SearchDocumentDownloadsAsync(Uri endpoint, string adminSecret, string? query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(adminSecret);

        var uri = new Uri(ToHttpUri(endpoint), string.IsNullOrWhiteSpace(query)
            ? "admin/document-downloads"
            : $"admin/document-downloads?query={Uri.EscapeDataString(query)}");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Add("X-Admin-Secret", adminSecret);

        using var response = await _httpClient.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("Relay odmítl zadané admin heslo.");
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<List<DocumentDownloadEntry>>(HttpJsonOptions, ct) ?? [];
    }

    private sealed record ManagedDeviceSummary(Guid DeviceId, string DisplayName, int? Role, List<string>? HiddenTabs, DateTimeOffset? LastSeenUtc, bool DocumentReviewer = false, string? AppVersion = null, bool PiccTeam = false, bool HasSharedLibraryKey = false, Guid? ArimContactId = null, string? ArimContactName = null, string? ArimContactEmail = null);

    private static Uri ToHttpUri(Uri wsEndpoint) => RelayHttp.ToHttpUri(wsEndpoint);

    public async Task<(string ConfigurationText, string? PickupCode)> CreateWireGuardClientAsync(Uri endpoint, string adminSecret, string memberName, SecureApp.Domain.Enums.Role? role = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(adminSecret);
        ArgumentException.ThrowIfNullOrWhiteSpace(memberName);

        var wireguardUri = new Uri(ToHttpUri(endpoint), "admin/wireguard/clients");
        using var request = new HttpRequestMessage(HttpMethod.Post, wireguardUri)
        {
            Content = JsonContent.Create(new { Name = memberName, Role = role is null ? null : (int?)role.Value }, options: HttpJsonOptions)
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
        return (result.ConfigurationText, result.PickupCode);
    }

    private sealed record WireGuardClientResult(string ConfigurationText, string? PickupCode);

    private sealed record InviteResponse(string Code, DateTimeOffset ExpiresAtUtc);

    /// <summary>Mirrors the relay's own <c>SecureApp.Relay.Contracts.RegisteredDeviceSummary</c> (2.1, 2026-09-17) — duplicated rather than shared, since this Presentation-layer client has no project reference to the Relay's own assembly (same reasoning as <see cref="InviteResponse"/> already established for the invite-code response shape).</summary>
    private sealed record RegisteredDeviceSummary(Guid Id, string DisplayName, DateTimeOffset CreatedAtUtc, string? DirectoryDisplayName, DateTimeOffset? LastActiveAtUtc, int PendingOutboxCount, string? Platform = null, Guid? ArimContactId = null, string? ArimContactName = null);
}
