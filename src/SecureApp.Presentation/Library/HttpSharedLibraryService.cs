using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;
using SecureApp.Presentation.Transport;

namespace SecureApp.Presentation.Library;

/// <inheritdoc cref="ISharedLibraryService"/>
/// <remarks>
/// Lives in Presentation (not Data) for the same reason <see cref="WebSocketMessageTransport"/>
/// does — needs <c>HttpClient</c> + relay endpoint/device-identity configuration — even though
/// nothing in this class is actually MAUI-specific.
///
/// Wire format for a library file's encrypted bytes: <c>Nonce (12) + AuthTag (16) + CipherText</c>
/// concatenated into one opaque blob — the relay only ever stores/serves this verbatim, it never
/// parses it. Fixed-length GCM nonce/tag sizes match <c>BouncyCastleCryptoService</c>'s own
/// constants throughout this codebase.
/// </remarks>
public sealed class HttpSharedLibraryService : ISharedLibraryService
{
    private const string SharedLibraryKeyVaultKey = "library:shared-key";
    private const int NonceLength = 12;
    private const int AuthTagLength = 16;

    private static readonly JsonSerializerOptions HttpJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ICryptoService _crypto;
    private readonly ISecureVaultKeyStore _vault;
    private readonly ITransportSettingsRepository _transportSettingsRepository;
    private readonly IServiceScopeFactory _scopeFactory;

    // HttpClient's own default Timeout (100s) is sized for ordinary API calls, not for uploading/
    // downloading a real document over a slow connection — this app is explicitly meant to be
    // usable off the home LAN, over a WireGuard tunnel on mobile data (see DEVELOPMENT_PLAN.md's
    // 2026-08-30/09-06 VPN verification notes), where a multi-megabyte file can genuinely take
    // longer than 100 seconds without anything being actually wrong. 5 minutes is a generous margin
    // for a document-sized file even on a slow cellular connection, not an attempt to support
    // arbitrarily large transfers.
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromMinutes(5) };

    public HttpSharedLibraryService(
        ICryptoService crypto,
        ISecureVaultKeyStore vault,
        ITransportSettingsRepository transportSettingsRepository,
        IServiceScopeFactory scopeFactory)
    {
        _crypto = crypto ?? throw new ArgumentNullException(nameof(crypto));
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _transportSettingsRepository = transportSettingsRepository ?? throw new ArgumentNullException(nameof(transportSettingsRepository));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    }

    public async Task<bool> HasSharedKeyAsync(CancellationToken ct = default)
        => await _vault.RetrieveSecretAsync(SharedLibraryKeyVaultKey, ct) is not null;

    public async Task<string> GenerateSharedKeyAsync(CancellationToken ct = default)
    {
        var key = RandomNumberGenerator.GetBytes(32);
        await _vault.StoreSecretAsync(SharedLibraryKeyVaultKey, key, ct);
        return Convert.ToBase64String(key);
    }

    public async Task ImportSharedKeyAsync(string keyBlob, CancellationToken ct = default)
    {
        byte[] key;
        try
        {
            key = Convert.FromBase64String(keyBlob.Trim());
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("Tohle nevypadá jako platný klíč sdílené knihovny.", ex);
        }

        if (key.Length != 32)
            throw new InvalidOperationException("Tohle nevypadá jako platný klíč sdílené knihovny (špatná délka).");

        await _vault.StoreSecretAsync(SharedLibraryKeyVaultKey, key, ct);
    }

    public async Task<string> ExportSharedKeyAsync(CancellationToken ct = default)
    {
        var key = await _vault.RetrieveSecretAsync(SharedLibraryKeyVaultKey, ct)
            ?? throw new InvalidOperationException("Na tomto zařízení zatím není nastavený žádný klíč sdílené knihovny.");
        return Convert.ToBase64String(key);
    }

    public async Task<SharedLibraryFileSummary> UploadAsync(string folderPath, string fileName, IReadOnlyList<string> tags, Stream content, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        var plaintext = buffer.ToArray();

        var key = await GetSharedKeyAsync(ct);
        var payload = await _crypto.EncryptWithKeyAsync(plaintext, Guid.NewGuid(), key, ct);
        var wireBytes = Combine(payload.Nonce, payload.AuthTag, payload.CipherText);
        var contentHash = Convert.ToHexStringLower(SHA256.HashData(wireBytes));

        var endpoint = await GetHttpEndpointAsync(ct);
        var tagsParam = string.Join(",", tags ?? []);
        var uri = new Uri(endpoint,
            $"library/files?folderPath={Uri.EscapeDataString(folderPath ?? string.Empty)}&fileName={Uri.EscapeDataString(fileName)}&tags={Uri.EscapeDataString(tagsParam)}&contentHash={contentHash}");

        using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = new ByteArrayContent(wireBytes) };
        await AddDeviceAuthAsync(request, ct);
        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<LibraryFileDto>(HttpJsonOptions, ct)
            ?? throw new InvalidOperationException("Relay vrátil prázdnou odpověď při nahrávání.");
        return ToSummary(dto);
    }

    public async Task<IReadOnlyList<SharedLibraryFileSummary>> SearchAsync(string? query = null, string? folderPath = null, string? tag = null, CancellationToken ct = default)
    {
        var endpoint = await GetHttpEndpointAsync(ct);
        var uri = new Uri(endpoint,
            $"library/files?query={Uri.EscapeDataString(query ?? string.Empty)}&folderPath={Uri.EscapeDataString(folderPath ?? string.Empty)}&tag={Uri.EscapeDataString(tag ?? string.Empty)}");

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        await AddDeviceAuthAsync(request, ct);
        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var dtos = await response.Content.ReadFromJsonAsync<List<LibraryFileDto>>(HttpJsonOptions, ct) ?? [];
        return dtos.Select(ToSummary).ToList();
    }

    public async Task<Document> DownloadAndImportAsync(Guid libraryFileId, Guid? localFolderId = null, CancellationToken ct = default)
    {
        var endpoint = await GetHttpEndpointAsync(ct);
        var uri = new Uri(endpoint, $"library/files/{libraryFileId}");

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        await AddDeviceAuthAsync(request, ct);
        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var fileName = response.Headers.TryGetValues("X-File-Name", out var fileNameValues)
            ? Uri.UnescapeDataString(fileNameValues.First())
            : $"{libraryFileId:N}.bin";

        var wireBytes = await response.Content.ReadAsByteArrayAsync(ct);
        var (nonce, authTag, cipherText) = Split(wireBytes);

        var key = await GetSharedKeyAsync(ct);
        var payload = new EncryptedPayload(libraryFileId, EncryptionAlgorithm.Aes256Gcm, cipherText, nonce, authTag);

        byte[] plaintext;
        try
        {
            plaintext = await _crypto.DecryptWithKeyAsync(payload, key, ct);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Tento soubor se nepodařilo dešifrovat — zkontrolujte, že váš klíč sdílené knihovny odpovídá klíči toho, kdo soubor nahrál.", ex);
        }

        using var scope = _scopeFactory.CreateScope();
        var importService = scope.ServiceProvider.GetRequiredService<IDocumentImportService>();
        using var plaintextStream = new MemoryStream(plaintext);
        return await importService.ImportAsync(plaintextStream, fileName, localFolderId, ct);
    }

    public async Task DeleteAsync(Guid libraryFileId, CancellationToken ct = default)
    {
        var endpoint = await GetHttpEndpointAsync(ct);
        var uri = new Uri(endpoint, $"library/files/{libraryFileId}");

        using var request = new HttpRequestMessage(HttpMethod.Delete, uri);
        await AddDeviceAuthAsync(request, ct);
        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    // --- Relay-mediated shared-library-key escrow (2026-09-11) — see ISharedLibraryService's own
    // remarks. Uses only the crypto primitives already in this codebase: ML-KEM EncapsulateAsync
    // against a recipient's raw directory public key + AES-256-GCM under a shared secret HKDF'd to a
    // fixed length. The relay never sees the plaintext key — only per-recipient ML-KEM ciphertext.

    private const string WrapKeyInfo = "library-key-wrap-v1";

    public async Task PublishWrappedKeyForMembersAsync(CancellationToken ct = default)
    {
        var keyBytes = await _vault.RetrieveSecretAsync(SharedLibraryKeyVaultKey, ct);
        if (keyBytes is null)
            return; // nothing to publish — this device doesn't have the key

        using var scope = _scopeFactory.CreateScope();
        var directory = scope.ServiceProvider.GetRequiredService<IContactDirectoryService>();
        var members = await directory.ListMembersAsync(ct);
        if (members.Count == 0)
            return;

        var endpoint = await GetHttpEndpointAsync(ct);
        var uri = new Uri(endpoint, "library/wrapped-keys");

        foreach (var member in members)
        {
            try
            {
                var (kemCipherText, sharedSecret) = await _crypto.EncapsulateAsync(member.PublicKey, ct);
                var wrapKey = await _crypto.DeriveKeyAsync(sharedSecret, null, WrapKeyInfo, 32, ct);
                var payload = await _crypto.EncryptWithKeyAsync(keyBytes, Guid.NewGuid(), wrapKey, ct);
                var blob = EncodeWrappedBlob(kemCipherText, payload.Nonce, payload.AuthTag, payload.CipherText);

                using var request = new HttpRequestMessage(HttpMethod.Post, uri)
                {
                    Content = JsonContent.Create(new { RecipientDeviceId = member.RelayDeviceId, WrappedBlob = blob }, options: HttpJsonOptions)
                };
                await AddDeviceAuthAsync(request, ct);
                using var response = await _httpClient.SendAsync(request, ct);
                response.EnsureSuccessStatusCode();
            }
            catch
            {
                // Best-effort per member — one failure must never stop wrapping for the rest.
            }
        }
    }

    public async Task<bool> TryImportWrappedKeyAsync(CancellationToken ct = default)
    {
        if (await _vault.RetrieveSecretAsync(SharedLibraryKeyVaultKey, ct) is not null)
            return false; // already have it

        var endpoint = await GetHttpEndpointAsync(ct);
        var uri = new Uri(endpoint, "library/wrapped-key");

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        await AddDeviceAuthAsync(request, ct);
        using var response = await _httpClient.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return false; // nothing escrowed for this device yet
        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<WrappedKeyDto>(HttpJsonOptions, ct);
        if (dto is null || string.IsNullOrEmpty(dto.WrappedBlob))
            return false;

        var (kemCipherText, nonce, authTag, cipherText) = DecodeWrappedBlob(dto.WrappedBlob);

        using var scope = _scopeFactory.CreateScope();
        var messaging = scope.ServiceProvider.GetRequiredService<IMessagingService>();
        var identityKeyId = await messaging.GetLocalIdentityKeyIdAsync(ct);

        var sharedSecret = await _crypto.DecapsulateAsync(identityKeyId, kemCipherText, ct);
        var wrapKey = await _crypto.DeriveKeyAsync(sharedSecret, null, WrapKeyInfo, 32, ct);
        var payload = new EncryptedPayload(Guid.NewGuid(), EncryptionAlgorithm.Aes256Gcm, cipherText, nonce, authTag);
        var keyBytes = await _crypto.DecryptWithKeyAsync(payload, wrapKey, ct);
        if (keyBytes.Length != 32)
            return false;

        await _vault.StoreSecretAsync(SharedLibraryKeyVaultKey, keyBytes, ct);
        return true;
    }

    private static string EncodeWrappedBlob(byte[] kem, byte[] nonce, byte[] authTag, byte[] cipherText)
    {
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            WriteChunk(w, kem);
            WriteChunk(w, nonce);
            WriteChunk(w, authTag);
            WriteChunk(w, cipherText);
        }
        return Convert.ToBase64String(stream.ToArray());
    }

    private static (byte[] Kem, byte[] Nonce, byte[] AuthTag, byte[] CipherText) DecodeWrappedBlob(string blob)
    {
        using var stream = new MemoryStream(Convert.FromBase64String(blob));
        using var r = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        var kem = ReadChunk(r);
        var nonce = ReadChunk(r);
        var authTag = ReadChunk(r);
        var cipherText = ReadChunk(r);
        return (kem, nonce, authTag, cipherText);
    }

    private static void WriteChunk(BinaryWriter w, byte[] data)
    {
        w.Write((ushort)data.Length);
        w.Write(data);
    }

    private static byte[] ReadChunk(BinaryReader r)
    {
        var length = r.ReadUInt16();
        return r.ReadBytes(length);
    }

    private sealed record WrappedKeyDto(string WrappedBlob);

    private async Task<byte[]> GetSharedKeyAsync(CancellationToken ct)
        => await _vault.RetrieveSecretAsync(SharedLibraryKeyVaultKey, ct)
            ?? throw new InvalidOperationException("Zatím nemáte nastavený klíč sdílené knihovny — nejprve ho vygenerujte nebo importujte v Nastavení.");

    private async Task<Uri> GetHttpEndpointAsync(CancellationToken ct)
    {
        var configuration = await _transportSettingsRepository.GetAsync(ct);
        var wsEndpoint = configuration?.EndpointUri
            ?? throw new InvalidOperationException("Zatím není nastavená adresa relay serveru — nastavte ji nejprve v Nastavení.");

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
            ?? throw new InvalidOperationException("Nejprve se zaregistrujte u relay serveru v Nastavení.");
        var secretBytes = await _vault.RetrieveSecretAsync(RelayDeviceVaultKeys.DeviceSecret, ct)
            ?? throw new InvalidOperationException("V úložišti chybí tajný klíč zařízení pro relay.");

        request.Headers.Add("X-Device-Id", deviceId.ToString());
        request.Headers.Add("X-Device-Secret", Encoding.UTF8.GetString(secretBytes));
    }

    private static byte[] Combine(byte[] nonce, byte[] authTag, byte[] cipherText)
    {
        var result = new byte[nonce.Length + authTag.Length + cipherText.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, nonce.Length);
        Buffer.BlockCopy(authTag, 0, result, nonce.Length, authTag.Length);
        Buffer.BlockCopy(cipherText, 0, result, nonce.Length + authTag.Length, cipherText.Length);
        return result;
    }

    private static (byte[] Nonce, byte[] AuthTag, byte[] CipherText) Split(byte[] wireBytes)
    {
        if (wireBytes.Length < NonceLength + AuthTagLength)
            throw new InvalidOperationException("Stažený soubor ze sdílené knihovny je příliš krátký, aby mohl obsahovat platná data.");

        var nonce = wireBytes[..NonceLength];
        var authTag = wireBytes[NonceLength..(NonceLength + AuthTagLength)];
        var cipherText = wireBytes[(NonceLength + AuthTagLength)..];
        return (nonce, authTag, cipherText);
    }

    private static SharedLibraryFileSummary ToSummary(LibraryFileDto dto) =>
        new(dto.Id, dto.FolderPath, dto.FileName, dto.Tags, dto.SizeBytes, dto.UploadedByDeviceId.ToString(), dto.UploadedAtUtc);

    private sealed record LibraryFileDto(Guid Id, string FolderPath, string FileName, List<string> Tags, long SizeBytes, Guid UploadedByDeviceId, DateTimeOffset UploadedAtUtc);
}
