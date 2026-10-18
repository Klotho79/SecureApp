using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;
using SecureApp.Presentation.Transport;

namespace SecureApp.Presentation.Library;

/// <inheritdoc cref="ICmsAnnotationService"/>
/// <remarks>
/// Bodies are encrypted client-side with the shared community key (vault key <c>library:shared-key</c>),
/// the same <c>Nonce(12)+AuthTag(16)+CipherText</c> base64 format as board posts, so the relay stores
/// ciphertext it cannot read. Per-recipient envelopes (so even other members without access can't decrypt)
/// are phase 6; until then the relay's visibility rules are the access control.
/// </remarks>
public sealed class HttpCmsAnnotationService : ICmsAnnotationService
{
    private const string SharedLibraryKeyVaultKey = "library:shared-key";
    private const int NonceLength = 12;
    private const int AuthTagLength = 16;
    private static readonly JsonSerializerOptions HttpJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ITransportSettingsRepository _transportSettingsRepository;
    private readonly ISecureVaultKeyStore _vault;
    private readonly ICryptoService _crypto;
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    public HttpCmsAnnotationService(ITransportSettingsRepository transportSettingsRepository, ISecureVaultKeyStore vault, ICryptoService crypto)
    {
        _transportSettingsRepository = transportSettingsRepository ?? throw new ArgumentNullException(nameof(transportSettingsRepository));
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _crypto = crypto ?? throw new ArgumentNullException(nameof(crypto));
    }

    public async Task<IReadOnlyList<CmsAnnotationView>> ListAsync(Guid documentId, CancellationToken ct = default)
    {
        var key = await _vault.RetrieveSecretAsync(SharedLibraryKeyVaultKey, ct);
        using var request = await CreateAsync(HttpMethod.Get, $"cms/documents/{documentId}/annotations", ct);
        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var dtos = await response.Content.ReadFromJsonAsync<List<AnnotationDto>>(HttpJsonOptions, ct) ?? [];

        var result = new List<CmsAnnotationView>(dtos.Count);
        foreach (var dto in dtos)
            result.Add(await ToViewAsync(dto, key, ct));
        return result;
    }

    public async Task<CmsAnnotationView> CreateAsync(Guid documentId, CmsAnchor anchor, string text, string audience, IReadOnlyList<Guid> recipientDeviceIds, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Poznámka nesmí být prázdná.");
        var key = await RequireKeyAsync(ct);

        var blob = await EncryptAsync(JsonSerializer.Serialize(new Body(anchor, text.Trim()), HttpJsonOptions), key, ct);
        using var request = await CreateAsync(HttpMethod.Post, $"cms/documents/{documentId}/annotations", ct);
        request.Content = JsonContent.Create(new { Id = Guid.NewGuid(), Audience = audience, RecipientDeviceIds = recipientDeviceIds, BodyBlob = blob }, options: HttpJsonOptions);
        return await SendForViewAsync(request, key, ct);
    }

    public async Task<CmsAnnotationView> ReplyAsync(Guid annotationId, string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Odpověď nesmí být prázdná.");
        var key = await RequireKeyAsync(ct);

        var blob = await EncryptAsync(JsonSerializer.Serialize(new Body(null, text.Trim()), HttpJsonOptions), key, ct);
        using var request = await CreateAsync(HttpMethod.Post, $"cms/annotations/{annotationId}/replies", ct);
        request.Content = JsonContent.Create(new { BodyBlob = blob }, options: HttpJsonOptions);
        return await SendForViewAsync(request, key, ct);
    }

    public async Task<CmsAnnotationView> ProposeAsync(Guid annotationId, string? note, CancellationToken ct = default)
    {
        var key = await _vault.RetrieveSecretAsync(SharedLibraryKeyVaultKey, ct);
        using var request = await CreateAsync(HttpMethod.Post, $"cms/annotations/{annotationId}/propose", ct);
        request.Content = JsonContent.Create(new { Note = note }, options: HttpJsonOptions);
        return await SendForViewAsync(request, key, ct);
    }

    public async Task<CmsAnnotationView> ModerateAsync(Guid annotationId, string decision, string? note, CancellationToken ct = default)
    {
        var key = await _vault.RetrieveSecretAsync(SharedLibraryKeyVaultKey, ct);
        using var request = await CreateAsync(HttpMethod.Post, $"cms/annotations/{annotationId}/moderate", ct);
        request.Content = JsonContent.Create(new { Decision = decision, Note = note }, options: HttpJsonOptions);
        return await SendForViewAsync(request, key, ct);
    }

    public async Task DeleteAsync(Guid annotationId, CancellationToken ct = default)
    {
        using var request = await CreateAsync(HttpMethod.Delete, $"cms/annotations/{annotationId}", ct);
        using var response = await _httpClient.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new InvalidOperationException("Poznámku smí smazat jen její autor nebo admin.");
        response.EnsureSuccessStatusCode();
    }

    public async Task SetHiddenAsync(Guid annotationId, bool hidden, CancellationToken ct = default)
    {
        using var request = await CreateAsync(hidden ? HttpMethod.Put : HttpMethod.Delete, $"cms/annotations/{annotationId}/hide", ct);
        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    private async Task<CmsAnnotationView> SendForViewAsync(HttpRequestMessage request, byte[]? key, CancellationToken ct)
    {
        using var response = await _httpClient.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden || response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new InvalidOperationException("Tuhle akci teď nelze provést (nemáte oprávnění, nebo už byla vyřízena).");
        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<AnnotationDto>(HttpJsonOptions, ct)
            ?? throw new InvalidOperationException("Relay vrátil prázdnou odpověď.");
        return await ToViewAsync(dto, key, ct);
    }

    private async Task<CmsAnnotationView> ToViewAsync(AnnotationDto dto, byte[]? key, CancellationToken ct)
    {
        var body = key is null ? null : await TryDecryptAsync(dto.BodyBlob, key, ct);
        var replies = new List<CmsAnnotationReplyView>(dto.Replies.Count);
        foreach (var r in dto.Replies)
        {
            var reply = key is null ? null : await TryDecryptAsync(r.BodyBlob, key, ct);
            replies.Add(new CmsAnnotationReplyView(r.Id, r.AuthorName, reply?.Text ?? Unreadable, r.CreatedAtUtc));
        }
        return new CmsAnnotationView(dto.Id, dto.DocumentId, dto.AuthorName, dto.Audience, body?.Anchor, body?.Text ?? Unreadable,
            dto.CreatedAtUtc, dto.HiddenByMe, dto.IsMine, replies, dto.Moderation, dto.CanModerate);
    }

    private const string Unreadable = "🔒 (nešlo dešifrovat — zkontrolujte klíč sdílené knihovny)";

    private async Task<byte[]> RequireKeyAsync(CancellationToken ct) =>
        await _vault.RetrieveSecretAsync(SharedLibraryKeyVaultKey, ct)
        ?? throw new InvalidOperationException("Chybí klíč sdílené knihovny — poznámky bez něj nejde šifrovat.");

    private async Task<string> EncryptAsync(string plaintext, byte[] key, CancellationToken ct)
    {
        var payload = await _crypto.EncryptWithKeyAsync(Encoding.UTF8.GetBytes(plaintext), Guid.NewGuid(), key, ct);
        var wire = new byte[payload.Nonce.Length + payload.AuthTag.Length + payload.CipherText.Length];
        payload.Nonce.CopyTo(wire, 0);
        payload.AuthTag.CopyTo(wire, payload.Nonce.Length);
        payload.CipherText.CopyTo(wire, payload.Nonce.Length + payload.AuthTag.Length);
        return Convert.ToBase64String(wire);
    }

    private async Task<Body?> TryDecryptAsync(string blob, byte[] key, CancellationToken ct)
    {
        try
        {
            var wire = Convert.FromBase64String(blob);
            if (wire.Length < NonceLength + AuthTagLength) return null;
            var payload = new EncryptedPayload(Guid.Empty, EncryptionAlgorithm.Aes256Gcm,
                wire[(NonceLength + AuthTagLength)..], wire[..NonceLength], wire[NonceLength..(NonceLength + AuthTagLength)]);
            var plaintext = await _crypto.DecryptWithKeyAsync(payload, key, ct);
            return JsonSerializer.Deserialize<Body>(Encoding.UTF8.GetString(plaintext), HttpJsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private async Task<HttpRequestMessage> CreateAsync(HttpMethod method, string path, CancellationToken ct)
    {
        var configuration = await _transportSettingsRepository.GetAsync(ct);
        var wsEndpoint = configuration?.EndpointUri
            ?? throw new InvalidOperationException("Zatím není nastavená adresa relay serveru — nastavte ji nejprve v Nastavení.");
        var scheme = wsEndpoint.Scheme switch { "ws" => "http", "wss" => "https", _ => wsEndpoint.Scheme };
        var endpoint = new UriBuilder(wsEndpoint) { Scheme = scheme, Port = wsEndpoint.Port }.Uri;

        var deviceId = configuration.AssignedDeviceId
            ?? throw new InvalidOperationException("Nejprve se zaregistrujte u relay serveru v Nastavení.");
        var secretBytes = await _vault.RetrieveSecretAsync(RelayDeviceVaultKeys.DeviceSecret, ct)
            ?? throw new InvalidOperationException("V úložišti chybí tajný klíč zařízení pro relay.");

        var request = new HttpRequestMessage(method, new Uri(endpoint, path));
        request.Headers.Add("X-Device-Id", deviceId.ToString());
        request.Headers.Add("X-Device-Secret", Encoding.UTF8.GetString(secretBytes));
        return request;
    }

    /// <summary>Decrypted payload: the anchor only on the annotation itself, null on replies.</summary>
    private sealed record Body(CmsAnchor? Anchor, string Text);

    private sealed record ReplyDto(Guid Id, Guid AuthorDeviceId, string AuthorName, string BodyBlob, DateTimeOffset CreatedAtUtc);

    private sealed record AnnotationDto(
        Guid Id, Guid DocumentId, Guid AuthorDeviceId, string AuthorName, string Audience, string BodyBlob, string Moderation,
        DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, bool HiddenByMe, bool IsMine, List<ReplyDto> Replies, bool CanModerate = false);
}
