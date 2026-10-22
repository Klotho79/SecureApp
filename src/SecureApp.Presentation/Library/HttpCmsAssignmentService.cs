using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;
using SecureApp.Presentation.Transport;

namespace SecureApp.Presentation.Library;

/// <inheritdoc cref="ICmsAssignmentService"/>
/// <remarks>Device-auth plumbing duplicated per this codebase's Http*Service convention (see <see cref="HttpLibraryReviewService"/>).</remarks>
public sealed class HttpCmsAssignmentService : ICmsAssignmentService
{
    private static readonly JsonSerializerOptions HttpJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ITransportSettingsRepository _transportSettingsRepository;
    private readonly ISecureVaultKeyStore _vault;
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    public HttpCmsAssignmentService(ITransportSettingsRepository transportSettingsRepository, ISecureVaultKeyStore vault)
    {
        _transportSettingsRepository = transportSettingsRepository ?? throw new ArgumentNullException(nameof(transportSettingsRepository));
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
    }

    public async Task<CmsAssignment> AssignAsync(Guid? documentId, Guid editorDeviceId, string title, string folderPath, CancellationToken ct = default)
    {
        using var request = await CreateAsync(HttpMethod.Post, "cms/editors", ct);
        request.Content = JsonContent.Create(new { DocumentId = documentId, EditorDeviceId = editorDeviceId, Title = title, FolderPath = folderPath }, options: HttpJsonOptions);
        using var response = await _httpClient.SendAsync(request, ct);
        EnsureAllowed(response);
        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<GrantDto>(HttpJsonOptions, ct)
            ?? throw new InvalidOperationException("Relay vrátil prázdnou odpověď při přidělení editora.");
        return ToModel(dto);
    }

    public async Task RevokeAsync(Guid documentId, Guid editorDeviceId, CancellationToken ct = default)
    {
        using var request = await CreateAsync(HttpMethod.Delete, $"cms/editors/{documentId}/{editorDeviceId}", ct);
        using var response = await _httpClient.SendAsync(request, ct);
        EnsureAllowed(response);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return; // already gone
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<CmsMember>> ListMembersAsync(CancellationToken ct = default)
    {
        using var request = await CreateAsync(HttpMethod.Get, "cms/members", ct);
        using var response = await _httpClient.SendAsync(request, ct);
        EnsureAllowed(response);
        response.EnsureSuccessStatusCode();
        var dtos = await response.Content.ReadFromJsonAsync<List<MemberDto>>(HttpJsonOptions, ct) ?? [];
        return dtos.Select(m => new CmsMember(m.DeviceId, m.DisplayName, m.Email)).ToList();
    }

    public Task<IReadOnlyList<CmsAssignment>> ListAllAsync(CancellationToken ct = default) => ListAsync("cms/editors", ct);

    public Task<IReadOnlyList<CmsAssignment>> ListMineAsync(CancellationToken ct = default) => ListAsync("cms/editors/mine", ct);

    private async Task<IReadOnlyList<CmsAssignment>> ListAsync(string path, CancellationToken ct)
    {
        using var request = await CreateAsync(HttpMethod.Get, path, ct);
        using var response = await _httpClient.SendAsync(request, ct);
        EnsureAllowed(response);
        response.EnsureSuccessStatusCode();
        var dtos = await response.Content.ReadFromJsonAsync<List<GrantDto>>(HttpJsonOptions, ct) ?? [];
        return dtos.Select(ToModel).ToList();
    }

    private static void EnsureAllowed(HttpResponseMessage response)
    {
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            throw new InvalidOperationException("K přidělování editorů nemáte oprávnění (smí jen Admin nebo Modifier).");
    }

    private static CmsAssignment ToModel(GrantDto g) =>
        new(g.DocumentId, g.EditorDeviceId, g.EditorDisplayName, g.Title, g.FolderPath, g.GrantedAtUtc, g.DocumentStatus);

    private Task<HttpRequestMessage> CreateAsync(HttpMethod method, string path, CancellationToken ct) => RelayHttp.CreateAuthorizedRequestAsync(method, path, _transportSettingsRepository, _vault, ct);

    private sealed record MemberDto(Guid DeviceId, string DisplayName, string? Email);

    private sealed record GrantDto(Guid DocumentId, Guid EditorDeviceId, string EditorDisplayName, string Title, string FolderPath, Guid GrantedBy, DateTimeOffset GrantedAtUtc, string? DocumentStatus);
}
