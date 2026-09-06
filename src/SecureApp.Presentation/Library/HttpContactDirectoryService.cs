using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;
using SecureApp.Presentation.Transport;

namespace SecureApp.Presentation.Library;

/// <inheritdoc cref="IContactDirectoryService"/>
/// <remarks>
/// Lives in Presentation (not Data), same reasoning as <see cref="HttpSharedLibraryService"/> —
/// needs <c>HttpClient</c> + relay endpoint/device-identity configuration. The device-auth header
/// helper and ws-&gt;http rewrite are duplicated from that class rather than shared — same
/// "duplicated, stays independently readable" call this codebase already made for
/// <c>ToHttpUri</c> across several transport classes.
/// </remarks>
public sealed class HttpContactDirectoryService : IContactDirectoryService
{
    private static readonly JsonSerializerOptions HttpJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ICurrentUserService _currentUserService;
    private readonly IMessagingService _messagingService;
    private readonly ISecureVaultKeyStore _vault;
    private readonly ITransportSettingsRepository _transportSettingsRepository;
    private readonly HttpClient _httpClient = new();

    public HttpContactDirectoryService(
        ICurrentUserService currentUserService,
        IMessagingService messagingService,
        ISecureVaultKeyStore vault,
        ITransportSettingsRepository transportSettingsRepository)
    {
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
        _messagingService = messagingService ?? throw new ArgumentNullException(nameof(messagingService));
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _transportSettingsRepository = transportSettingsRepository ?? throw new ArgumentNullException(nameof(transportSettingsRepository));
    }

    public async Task PublishSelfAsync(CancellationToken ct = default)
    {
        var publicKey = await _messagingService.GetLocalIdentityPublicKeyAsync(ct);
        var endpoint = await GetHttpEndpointAsync(ct);
        var uri = new Uri(endpoint, "directory/publish");

        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = JsonContent.Create(
                new { DisplayName = _currentUserService.Current.DisplayName, PublicKeyBase64 = Convert.ToBase64String(publicKey) },
                options: HttpJsonOptions)
        };
        await AddDeviceAuthAsync(request, ct);
        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<DirectoryMember>> ListMembersAsync(CancellationToken ct = default)
    {
        var endpoint = await GetHttpEndpointAsync(ct);
        var uri = new Uri(endpoint, "directory/members");

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        await AddDeviceAuthAsync(request, ct);
        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var dtos = await response.Content.ReadFromJsonAsync<List<DirectoryMemberDto>>(HttpJsonOptions, ct) ?? [];
        return dtos.Select(d => new DirectoryMember(d.DeviceId, d.DisplayName, Convert.FromBase64String(d.PublicKeyBase64))).ToList();
    }

    private async Task<Uri> GetHttpEndpointAsync(CancellationToken ct)
    {
        var configuration = await _transportSettingsRepository.GetAsync(ct);
        var wsEndpoint = configuration?.EndpointUri
            ?? throw new InvalidOperationException("No relay endpoint is configured yet — set one up in Settings first.");

        var scheme = wsEndpoint.Scheme switch
        {
            "ws" => "http",
            "wss" => "https",
            _ => wsEndpoint.Scheme
        };
        return new UriBuilder(wsEndpoint) { Scheme = scheme, Port = wsEndpoint.Port }.Uri;
    }

    private async Task AddDeviceAuthAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var configuration = await _transportSettingsRepository.GetAsync(ct);
        var deviceId = configuration?.AssignedDeviceId
            ?? throw new InvalidOperationException("Register with a relay in Settings first.");
        var secretBytes = await _vault.RetrieveSecretAsync(RelayDeviceVaultKeys.DeviceSecret, ct)
            ?? throw new InvalidOperationException("Relay device secret is missing from the vault.");

        request.Headers.Add("X-Device-Id", deviceId.ToString());
        request.Headers.Add("X-Device-Secret", Encoding.UTF8.GetString(secretBytes));
    }

    private sealed record DirectoryMemberDto(Guid DeviceId, string DisplayName, string PublicKeyBase64);
}
