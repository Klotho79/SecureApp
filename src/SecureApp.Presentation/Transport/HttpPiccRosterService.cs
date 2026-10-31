using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;
using SecureApp.Presentation.Workplace;

namespace SecureApp.Presentation.Transport;

/// <inheritdoc cref="IPiccRosterService"/>
/// <remarks>
/// Talks to the SAME <c>library/files</c> endpoints <see cref="Library.HttpSharedLibraryService"/>
/// uses (identical wire format: <c>Nonce(12)+AuthTag(16)+CipherText</c>, shared-library-key AES-GCM)
/// but keeps the decrypted bytes in memory for <see cref="PiccRosterParser"/> instead of importing a
/// local <c>Document</c> — nothing here should show up in the ordinary Document Library. The roster
/// is uploaded <c>listed: false</c> (an unlisted shared-library file, by id only), and that id is
/// never handed to a device the relay doesn't consider picc_team/Admin (see Program.cs's
/// /picc/roster-pointer) — the real confidentiality boundary for the nurses' names, not merely a
/// hidden UI card.
/// </remarks>
public sealed class HttpPiccRosterService : IPiccRosterService
{
    private const string SharedLibraryKeyVaultKey = "library:shared-key";
    private const string PiccRosterFolder = "PICC";
    private const string PiccRosterTag = "picc-roster";
    private const int NonceLength = 12;
    private const int AuthTagLength = 16;
    private static readonly JsonSerializerOptions HttpJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ITransportSettingsRepository _transportSettingsRepository;
    private readonly ISecureVaultKeyStore _vault;
    private readonly ICryptoService _crypto;
    private readonly IDevicePolicyService _devicePolicyService;
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromMinutes(2) };

    public HttpPiccRosterService(
        ITransportSettingsRepository transportSettingsRepository,
        ISecureVaultKeyStore vault,
        ICryptoService crypto,
        IDevicePolicyService devicePolicyService)
    {
        _transportSettingsRepository = transportSettingsRepository ?? throw new ArgumentNullException(nameof(transportSettingsRepository));
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _crypto = crypto ?? throw new ArgumentNullException(nameof(crypto));
        _devicePolicyService = devicePolicyService ?? throw new ArgumentNullException(nameof(devicePolicyService));
    }

    public async Task<bool> CanManageRosterAsync(CancellationToken ct = default)
    {
        var policy = await _devicePolicyService.GetMyPolicyAsync(ct);
        return policy.IsPiccTeam || policy.Role == Role.Admin;
    }

    public async Task<PiccRosterSnapshot> UploadRosterAsync(Stream xlsxContent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(xlsxContent);

        using var buffer = new MemoryStream();
        await xlsxContent.CopyToAsync(buffer, ct);
        var plaintext = buffer.ToArray();

        PiccRosterSnapshot snapshot;
        using (var parseStream = new MemoryStream(plaintext))
            snapshot = PiccRosterParser.Parse(parseStream, DateOnly.FromDateTime(DateTime.Today));

        var key = await GetSharedKeyAsync(ct);
        var (baseUri, deviceId, secret) = await GetAuthAsync(ct);

        // 1. The full file, unlisted — see this class's own remarks on why that's the real boundary.
        var payload = await _crypto.EncryptWithKeyAsync(plaintext, Guid.NewGuid(), key, ct);
        var wireBytes = SharedKeyWire.Combine(payload.Nonce, payload.AuthTag, payload.CipherText);
        var contentHash = Convert.ToHexStringLower(SHA256.HashData(wireBytes));
        var fileName = $"picc-roster-{DateTime.UtcNow:yyyyMMdd-HHmmss}.xlsx";
        var uploadUri = new Uri(baseUri,
            $"library/files?folderPath={Uri.EscapeDataString(PiccRosterFolder)}&fileName={Uri.EscapeDataString(fileName)}&tags={Uri.EscapeDataString(PiccRosterTag)}&contentHash={contentHash}&listed=false");
        using (var uploadRequest = new HttpRequestMessage(HttpMethod.Post, uploadUri) { Content = new ByteArrayContent(wireBytes) })
        {
            AddAuth(uploadRequest, deviceId, secret);
            using var uploadResponse = await _httpClient.SendAsync(uploadRequest, ct);
            if (uploadResponse.StatusCode == HttpStatusCode.Forbidden)
                throw new InvalidOperationException("Nemáte oprávnění nahrávat rozpis PICC (chybí příznak „PICC tým“).");
            uploadResponse.EnsureSuccessStatusCode();
            var dto = await uploadResponse.Content.ReadFromJsonAsync<LibraryFileDto>(HttpJsonOptions, ct)
                ?? throw new InvalidOperationException("Relay vrátil prázdnou odpověď při nahrávání rozpisu.");

            // 2. Point the relay at it — picc_team/Admin gate enforced server-side (see Program.cs).
            var pointerUri = new Uri(baseUri, "picc/roster-pointer");
            using var pointerRequest = new HttpRequestMessage(HttpMethod.Post, pointerUri)
            {
                Content = JsonContent.Create(new { LibraryFileId = dto.Id }, options: HttpJsonOptions)
            };
            AddAuth(pointerRequest, deviceId, secret);
            using var pointerResponse = await _httpClient.SendAsync(pointerRequest, ct);
            if (pointerResponse.StatusCode == HttpStatusCode.Forbidden)
                throw new InvalidOperationException("Nemáte oprávnění nahrávat rozpis PICC (chybí příznak „PICC tým“).");
            pointerResponse.EnsureSuccessStatusCode();
        }

        // 3. Publish just today's doctor name — public, every device learns it from here.
        await PublishStatusAsync(snapshot.Today?.DoctorName, snapshot.Today?.Date, key, baseUri, deviceId, secret, ct);
        PiccRosterStore.Save(snapshot.Today?.DoctorName, snapshot.Today?.Date);

        return snapshot;
    }

    public async Task<PiccStatus?> GetCurrentStatusAsync(CancellationToken ct = default)
    {
        try
        {
            var key = await _vault.RetrieveSecretAsync(SharedLibraryKeyVaultKey, ct);
            if (key is null) return null; // no key set up on this device — nothing to decrypt with

            var (baseUri, deviceId, secret) = await GetAuthAsync(ct);
            var uri = new Uri(baseUri, "picc/status");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            AddAuth(request, deviceId, secret);
            using var response = await _httpClient.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.NotFound) return null; // nobody has ever published

            response.EnsureSuccessStatusCode();
            var dto = await response.Content.ReadFromJsonAsync<PiccStatusDto>(HttpJsonOptions, ct);
            if (dto is null) return null;

            var json = await DecryptTextAsync(dto.ContentBlob, key, ct);
            var inner = JsonSerializer.Deserialize<StatusPayload>(json, HttpJsonOptions);
            if (inner is null) return null;

            var asOf = inner.AsOfDate is { Length: > 0 } raw && DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? parsed
                : (DateOnly?)null;
            return new PiccStatus(inner.DoctorName, asOf, dto.UpdatedAtUtc);
        }
        catch
        {
            return null; // best-effort — a network/decrypt blip must never crash the widget sync
        }
    }

    public async Task<PiccRosterSnapshot?> GetFullRosterAsync(CancellationToken ct = default)
    {
        var (baseUri, deviceId, secret) = await GetAuthAsync(ct);

        var pointerUri = new Uri(baseUri, "picc/roster-pointer");
        using var pointerRequest = new HttpRequestMessage(HttpMethod.Get, pointerUri);
        AddAuth(pointerRequest, deviceId, secret);
        using var pointerResponse = await _httpClient.SendAsync(pointerRequest, ct);
        if (pointerResponse.StatusCode == HttpStatusCode.Forbidden)
            throw new InvalidOperationException("Nemáte oprávnění vidět rozpis PICC (chybí příznak „PICC tým“).");
        if (pointerResponse.StatusCode == HttpStatusCode.NotFound)
            return null; // nobody has ever uploaded one
        pointerResponse.EnsureSuccessStatusCode();
        var pointer = await pointerResponse.Content.ReadFromJsonAsync<PiccRosterPointerDto>(HttpJsonOptions, ct)
            ?? throw new InvalidOperationException("Relay vrátil prázdnou odpověď pro ukazatel rozpisu PICC.");

        var key = await GetSharedKeyAsync(ct);
        var fileUri = new Uri(baseUri, $"library/files/{pointer.LibraryFileId}");
        using var fileRequest = new HttpRequestMessage(HttpMethod.Get, fileUri);
        AddAuth(fileRequest, deviceId, secret);
        using var fileResponse = await _httpClient.SendAsync(fileRequest, ct);
        fileResponse.EnsureSuccessStatusCode();

        var wireBytes = await fileResponse.Content.ReadAsByteArrayAsync(ct);
        var (nonce, authTag, cipherText) = SharedKeyWire.Split(wireBytes, "Stažená data jsou příliš krátká, aby mohla obsahovat platný obsah.");
        var payload = new EncryptedPayload(pointer.LibraryFileId, EncryptionAlgorithm.Aes256Gcm, cipherText, nonce, authTag);

        byte[] plaintext;
        try
        {
            plaintext = await _crypto.DecryptWithKeyAsync(payload, key, ct);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Rozpis PICC se nepodařilo dešifrovat — zkontrolujte klíč sdílené knihovny.", ex);
        }

        using var parseStream = new MemoryStream(plaintext);
        return PiccRosterParser.Parse(parseStream, DateOnly.FromDateTime(DateTime.Today));
    }

    private async Task PublishStatusAsync(string? doctorName, DateOnly? asOfDate, byte[] key, Uri baseUri, Guid deviceId, string secret, CancellationToken ct)
    {
        var innerJson = JsonSerializer.Serialize(new StatusPayload(doctorName, asOfDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), HttpJsonOptions);
        var payload = await _crypto.EncryptWithKeyAsync(Encoding.UTF8.GetBytes(innerJson), Guid.NewGuid(), key, ct);
        var blob = Convert.ToBase64String(SharedKeyWire.Combine(payload.Nonce, payload.AuthTag, payload.CipherText));

        var uri = new Uri(baseUri, "picc/status");
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = JsonContent.Create(new { ContentBlob = blob }, options: HttpJsonOptions)
        };
        AddAuth(request, deviceId, secret);
        using var response = await _httpClient.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw new InvalidOperationException("Nemáte oprávnění publikovat stav PICC (chybí příznak „PICC tým“).");
        response.EnsureSuccessStatusCode();
    }

    private async Task<string> DecryptTextAsync(string blob, byte[] key, CancellationToken ct)
    {
        var wire = Convert.FromBase64String(blob);
        var (nonce, authTag, cipherText) = SharedKeyWire.Split(wire, "Stažená data jsou příliš krátká, aby mohla obsahovat platný obsah.");
        var payload = new EncryptedPayload(Guid.Empty, EncryptionAlgorithm.Aes256Gcm, cipherText, nonce, authTag);
        var plaintext = await _crypto.DecryptWithKeyAsync(payload, key, ct);
        return Encoding.UTF8.GetString(plaintext);
    }

    private async Task<byte[]> GetSharedKeyAsync(CancellationToken ct)
        => await _vault.RetrieveSecretAsync(SharedLibraryKeyVaultKey, ct)
            ?? throw new InvalidOperationException("Nemáte klíč sdílené knihovny — nejprve ho v Nastavení vygenerujte nebo importujte.");

    private async Task<(Uri BaseUri, Guid DeviceId, string Secret)> GetAuthAsync(CancellationToken ct)
    {
        var configuration = await _transportSettingsRepository.GetAsync(ct);
        var deviceId = configuration?.AssignedDeviceId
            ?? throw new InvalidOperationException("Nejprve se zaregistrujte u relay serveru v Nastavení.");
        var wsEndpoint = configuration?.EndpointUri
            ?? throw new InvalidOperationException("Není nastavená adresa relay serveru.");
        var secretBytes = await _vault.RetrieveSecretAsync(RelayDeviceVaultKeys.DeviceSecret, ct)
            ?? throw new InvalidOperationException("V úložišti chybí tajný klíč zařízení.");

        var baseUri = RelayHttp.ToHttpUri(wsEndpoint);
        return (baseUri, deviceId, Encoding.UTF8.GetString(secretBytes));
    }

    private static void AddAuth(HttpRequestMessage request, Guid deviceId, string secret)
    {
        request.Headers.Add("X-Device-Id", deviceId.ToString());
        request.Headers.Add("X-Device-Secret", secret);
    }

            private sealed record LibraryFileDto(Guid Id, string FolderPath, string FileName, List<string> Tags, long SizeBytes, Guid UploadedByDeviceId, DateTimeOffset UploadedAtUtc);
    private sealed record PiccStatusDto(string ContentBlob, DateTimeOffset UpdatedAtUtc);
    private sealed record PiccRosterPointerDto(Guid LibraryFileId, DateTimeOffset UpdatedAtUtc);
    private sealed record StatusPayload(string? DoctorName, string? AsOfDate);
}
