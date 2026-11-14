using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;
using SecureApp.Presentation.Transport;

namespace SecureApp.Presentation.Library;

/// <inheritdoc cref="ICmsAssignmentService"/>
/// <remarks>Endpoint and device auth come from <see cref="RelayServiceBase"/>.</remarks>
public sealed class HttpCmsAssignmentService(ITransportSettingsRepository transportSettingsRepository, ISecureVaultKeyStore vault)
    : RelayServiceBase(vault, transportSettingsRepository, TimeSpan.FromSeconds(30)), ICmsAssignmentService
{
    public async Task<CmsAssignment> AssignAsync(Guid? documentId, Guid editorDeviceId, string title, string folderPath, CancellationToken ct = default)
    {
        using var request = await CreateRequestAsync(HttpMethod.Post, "cms/editors",
            new { DocumentId = documentId, EditorDeviceId = editorDeviceId, Title = title, FolderPath = folderPath }, ct);
        using var response = await Http.SendAsync(request, ct);
        EnsureAllowed(response);
        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<GrantDto>(HttpJsonOptions, ct)
            ?? throw new InvalidOperationException("Relay vrátil prázdnou odpověď při přidělení editora.");
        return ToModel(dto);
    }

    public async Task RevokeAsync(Guid documentId, Guid editorDeviceId, CancellationToken ct = default)
    {
        using var request = await CreateRequestAsync(HttpMethod.Delete, $"cms/editors/{documentId}/{editorDeviceId}", null, ct);
        using var response = await Http.SendAsync(request, ct);
        EnsureAllowed(response);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return; // already gone
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<CmsMember>> ListMembersAsync(CancellationToken ct = default)
    {
        using var request = await CreateRequestAsync(HttpMethod.Get, "cms/members", null, ct);
        using var response = await Http.SendAsync(request, ct);
        EnsureAllowed(response);
        response.EnsureSuccessStatusCode();
        var dtos = await response.Content.ReadFromJsonAsync<List<MemberDto>>(HttpJsonOptions, ct) ?? [];
        return dtos.Select(m => new CmsMember(m.DeviceId, m.DisplayName, m.Email)).ToList();
    }

    public Task<IReadOnlyList<CmsAssignment>> ListAllAsync(CancellationToken ct = default) => ListAsync("cms/editors", ct);

    public Task<IReadOnlyList<CmsAssignment>> ListMineAsync(CancellationToken ct = default) => ListAsync("cms/editors/mine", ct);

    private async Task<IReadOnlyList<CmsAssignment>> ListAsync(string path, CancellationToken ct)
    {
        using var request = await CreateRequestAsync(HttpMethod.Get, path, null, ct);
        using var response = await Http.SendAsync(request, ct);
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

    private sealed record MemberDto(Guid DeviceId, string DisplayName, string? Email);

    private sealed record GrantDto(Guid DocumentId, Guid EditorDeviceId, string EditorDisplayName, string Title, string FolderPath, Guid GrantedBy, DateTimeOffset GrantedAtUtc, string? DocumentStatus);
}
