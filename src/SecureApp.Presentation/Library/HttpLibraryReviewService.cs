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
/// <c>HttpClient</c> + relay endpoint/device-identity configuration. Endpoint and device-auth plumbing
/// is shared via <see cref="RelayHttp"/>.
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

    public async Task<LibraryDocumentSummary> CreateDraftAsync(string title, string folderPath, Guid libraryFileId, string? changeNote, CancellationToken ct = default, Guid? documentId = null)
    {
        var endpoint = await GetHttpEndpointAsync(ct);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint, "library-documents"))
        {
            Content = JsonContent.Create(new { Title = title, FolderPath = folderPath, LibraryFileId = libraryFileId, ChangeNote = changeNote, Id = documentId }, options: HttpJsonOptions),
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

    public async Task<IReadOnlyList<InProgressDocument>> GetInProgressAsync(CancellationToken ct = default)
    {
        var endpoint = await GetHttpEndpointAsync(ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(endpoint, "library-documents/in-progress"));
        await AddDeviceAuthAsync(request, ct);
        using var response = await _httpClient.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            return []; // not Admin/Modifier/reviewer — an empty overview, same tolerance as the review queue
        response.EnsureSuccessStatusCode();

        var dtos = await response.Content.ReadFromJsonAsync<List<InProgressDto>>(HttpJsonOptions, ct) ?? [];
        return dtos.Select(d => new InProgressDocument(ToSummary(d.Document), d.AuthorName)).ToList();
    }

    private sealed record InProgressDto(LibraryDocumentDto Document, string AuthorName);

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

    private Task<Uri> GetHttpEndpointAsync(CancellationToken ct) => RelayHttp.GetHttpEndpointAsync(_transportSettingsRepository, ct);

    private Task AddDeviceAuthAsync(HttpRequestMessage request, CancellationToken ct) => RelayHttp.AddDeviceAuthAsync(request, _transportSettingsRepository, _vault, ct);

    private sealed record LibraryDocumentDto(
        Guid Id, string Title, string FolderPath, string Status,
        Guid? CurrentVersionId, Guid? CurrentLibraryFileId,
        Guid CreatedByDeviceId, Guid? SubmittedByDeviceId, DateTimeOffset? SubmittedAtUtc,
        DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

    private sealed record LibraryDocumentVersionDto(Guid Id, Guid LibraryDocumentId, int VersionNumber, Guid LibraryFileId, Guid AuthorDeviceId, DateTimeOffset CreatedAtUtc, string? ChangeNote);

    private sealed record LibraryDocumentReviewEntryDto(Guid Id, Guid LibraryDocumentId, Guid VersionId, Guid ReviewerDeviceId, string Decision, string? Comment, DateTimeOffset DecidedAtUtc);

    private sealed record LibraryDocumentDetailDto(LibraryDocumentDto Document, List<LibraryDocumentVersionDto> Versions, List<LibraryDocumentReviewEntryDto> Reviews);
}
