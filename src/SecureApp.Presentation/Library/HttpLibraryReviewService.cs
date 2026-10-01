using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;
using SecureApp.Presentation.Transport;

namespace SecureApp.Presentation.Library;

/// <inheritdoc cref="ILibraryReviewService"/>
/// <remarks>
/// Lives in Presentation alongside <see cref="HttpSharedLibraryService"/> for the same reason — needs
/// <c>HttpClient</c> + relay endpoint/device-identity configuration. Device-auth header helper is
/// duplicated rather than shared, following this codebase's established per-Http*Service convention
/// (see <see cref="HttpSharedLibraryService"/>'s own <c>AddDeviceAuthAsync</c>).
/// </remarks>
public sealed class HttpLibraryReviewService : ILibraryReviewService
{
    private static readonly JsonSerializerOptions HttpJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ITransportSettingsRepository _transportSettingsRepository;
    private readonly ISecureVaultKeyStore _vault;
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    public HttpLibraryReviewService(ITransportSettingsRepository transportSettingsRepository, ISecureVaultKeyStore vault)
    {
        _transportSettingsRepository = transportSettingsRepository ?? throw new ArgumentNullException(nameof(transportSettingsRepository));
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
    }

    public async Task<LibraryDocumentSummary> CreateDraftAsync(string title, string folderPath, Guid libraryFileId, string? changeNote, CancellationToken ct = default)
    {
        var endpoint = await GetHttpEndpointAsync(ct);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint, "library-documents"))
        {
            Content = JsonContent.Create(new { Title = title, FolderPath = folderPath, LibraryFileId = libraryFileId, ChangeNote = changeNote }, options: HttpJsonOptions),
        };
        await AddDeviceAuthAsync(request, ct);
        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<LibraryDocumentDto>(HttpJsonOptions, ct)
            ?? throw new InvalidOperationException("Relay vrátil prázdnou odpověď při vytváření konceptu dokumentu.");
        return ToSummary(dto);
    }

    public async Task<LibraryDocumentSummary> AddVersionAsync(Guid documentId, Guid libraryFileId, string? changeNote, CancellationToken ct = default)
    {
        var endpoint = await GetHttpEndpointAsync(ct);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint, $"library-documents/{documentId}/versions"))
        {
            Content = JsonContent.Create(new { LibraryFileId = libraryFileId, ChangeNote = changeNote }, options: HttpJsonOptions),
        };
        await AddDeviceAuthAsync(request, ct);
        using var response = await _httpClient.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new InvalidOperationException("Tento dokument na relay serveru už neexistuje.");
        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<LibraryDocumentDto>(HttpJsonOptions, ct)
            ?? throw new InvalidOperationException("Relay vrátil prázdnou odpověď při přidávání nové verze.");
        return ToSummary(dto);
    }

    public async Task<LibraryDocumentSummary> SubmitForReviewAsync(Guid documentId, CancellationToken ct = default)
    {
        var endpoint = await GetHttpEndpointAsync(ct);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint, $"library-documents/{documentId}/submit"));
        await AddDeviceAuthAsync(request, ct);
        using var response = await _httpClient.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new InvalidOperationException("Tento dokument na relay serveru už neexistuje.");
        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<LibraryDocumentDto>(HttpJsonOptions, ct)
            ?? throw new InvalidOperationException("Relay vrátil prázdnou odpověď při odesílání ke schválení.");
        return ToSummary(dto);
    }

    public async Task<IReadOnlyList<LibraryDocumentSummary>> GetMyDocumentsAsync(CancellationToken ct = default)
    {
        var endpoint = await GetHttpEndpointAsync(ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(endpoint, "library-documents/mine"));
        await AddDeviceAuthAsync(request, ct);
        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var dtos = await response.Content.ReadFromJsonAsync<List<LibraryDocumentDto>>(HttpJsonOptions, ct) ?? [];
        return dtos.Select(ToSummary).ToList();
    }

    public async Task<IReadOnlyList<LibraryDocumentSummary>> GetPendingReviewAsync(CancellationToken ct = default)
    {
        var endpoint = await GetHttpEndpointAsync(ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(endpoint, "library-documents/pending"));
        await AddDeviceAuthAsync(request, ct);
        using var response = await _httpClient.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            return []; // not a reviewer/admin — treat as an empty queue rather than throwing
        response.EnsureSuccessStatusCode();

        var dtos = await response.Content.ReadFromJsonAsync<List<LibraryDocumentDto>>(HttpJsonOptions, ct) ?? [];
        return dtos.Select(ToSummary).ToList();
    }

    public async Task<LibraryDocumentDetail> GetDetailAsync(Guid documentId, CancellationToken ct = default)
    {
        var endpoint = await GetHttpEndpointAsync(ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(endpoint, $"library-documents/{documentId}"));
        await AddDeviceAuthAsync(request, ct);
        using var response = await _httpClient.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new InvalidOperationException("Tento dokument na relay serveru už neexistuje.");
        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<LibraryDocumentDetailDto>(HttpJsonOptions, ct)
            ?? throw new InvalidOperationException("Relay vrátil prázdnou odpověď.");
        return new LibraryDocumentDetail(
            ToSummary(dto.Document),
            dto.Versions.Select(v => new LibraryDocumentVersionSummary(v.Id, v.LibraryDocumentId, v.VersionNumber, v.LibraryFileId, v.AuthorDeviceId.ToString(), v.CreatedAtUtc, v.ChangeNote)).ToList(),
            dto.Reviews.Select(r => new LibraryDocumentReviewEntry(r.Id, r.LibraryDocumentId, r.VersionId, r.ReviewerDeviceId.ToString(), r.Decision, r.Comment, r.DecidedAtUtc)).ToList());
    }

    public async Task ReviewAsync(Guid documentId, bool approve, string? comment, CancellationToken ct = default)
    {
        if (!approve && string.IsNullOrWhiteSpace(comment))
            throw new InvalidOperationException("Při zamítnutí je komentář povinný.");

        var endpoint = await GetHttpEndpointAsync(ct);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint, $"library-documents/{documentId}/review"))
        {
            Content = JsonContent.Create(new { Decision = approve ? "Approved" : "Rejected", Comment = comment }, options: HttpJsonOptions),
        };
        await AddDeviceAuthAsync(request, ct);
        using var response = await _httpClient.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            throw new InvalidOperationException("Nemáte oprávnění tento dokument schvalovat (buď nejste Reviewer, nebo jde o váš vlastní návrh).");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new InvalidOperationException("Tento dokument na relay serveru už neexistuje.");
        response.EnsureSuccessStatusCode();
    }

    private static LibraryDocumentSummary ToSummary(LibraryDocumentDto dto) => new(
        dto.Id, dto.Title, dto.FolderPath, Enum.Parse<LibraryDocumentStatus>(dto.Status),
        dto.CurrentVersionId, dto.CurrentLibraryFileId,
        dto.CreatedByDeviceId.ToString(), dto.SubmittedByDeviceId?.ToString(), dto.SubmittedAtUtc,
        dto.CreatedAtUtc, dto.UpdatedAtUtc);

    private async Task<Uri> GetHttpEndpointAsync(CancellationToken ct)
    {
        var configuration = await _transportSettingsRepository.GetAsync(ct);
        var wsEndpoint = configuration?.EndpointUri
            ?? throw new InvalidOperationException("Zatím není nastavená adresa relay serveru — nastavte ji nejprve v Nastavení.");

        var scheme = wsEndpoint.Scheme switch { "ws" => "http", "wss" => "https", _ => wsEndpoint.Scheme };
        return new UriBuilder(wsEndpoint) { Scheme = scheme, Port = wsEndpoint.Port }.Uri;
    }

    private async Task AddDeviceAuthAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var configuration = await _transportSettingsRepository.GetAsync(ct);
        var deviceId = configuration?.AssignedDeviceId
            ?? throw new InvalidOperationException("Nejprve se zaregistrujte u relay serveru v Nastavení.");
        var secretBytes = await _vault.RetrieveSecretAsync(RelayDeviceVaultKeys.DeviceSecret, ct)
            ?? throw new InvalidOperationException("V úložišti chybí tajný klíč zařízení pro relay.");

        request.Headers.Add("X-Device-Id", deviceId.ToString());
        request.Headers.Add("X-Device-Secret", Encoding.UTF8.GetString(secretBytes));
    }

    private sealed record LibraryDocumentDto(
        Guid Id, string Title, string FolderPath, string Status,
        Guid? CurrentVersionId, Guid? CurrentLibraryFileId,
        Guid CreatedByDeviceId, Guid? SubmittedByDeviceId, DateTimeOffset? SubmittedAtUtc,
        DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

    private sealed record LibraryDocumentVersionDto(Guid Id, Guid LibraryDocumentId, int VersionNumber, Guid LibraryFileId, Guid AuthorDeviceId, DateTimeOffset CreatedAtUtc, string? ChangeNote);

    private sealed record LibraryDocumentReviewEntryDto(Guid Id, Guid LibraryDocumentId, Guid VersionId, Guid ReviewerDeviceId, string Decision, string? Comment, DateTimeOffset DecidedAtUtc);

    private sealed record LibraryDocumentDetailDto(LibraryDocumentDto Document, List<LibraryDocumentVersionDto> Versions, List<LibraryDocumentReviewEntryDto> Reviews);
}
