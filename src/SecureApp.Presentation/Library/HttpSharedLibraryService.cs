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
            throw new InvalidOperationException("That doesn't look like a valid shared library key.", ex);
        }

        if (key.Length != 32)
            throw new InvalidOperationException("That doesn't look like a valid shared library key (wrong length).");

        await _vault.StoreSecretAsync(SharedLibraryKeyVaultKey, key, ct);
    }

    public async Task<string> ExportSharedKeyAsync(CancellationToken ct = default)
    {
        var key = await _vault.RetrieveSecretAsync(SharedLibraryKeyVaultKey, ct)
            ?? throw new InvalidOperationException("No shared library key is set up yet on this device.");
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
            ?? throw new InvalidOperationException("Relay returned an empty upload response.");
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
            throw new InvalidOperationException("Could not decrypt this library file — check your shared library key matches the uploader's.", ex);
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

    private async Task<byte[]> GetSharedKeyAsync(CancellationToken ct)
        => await _vault.RetrieveSecretAsync(SharedLibraryKeyVaultKey, ct)
            ?? throw new InvalidOperationException("No shared library key is set up yet — generate or import one in Settings first.");

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
            throw new InvalidOperationException("Downloaded library file is too short to contain a valid envelope.");

        var nonce = wireBytes[..NonceLength];
        var authTag = wireBytes[NonceLength..(NonceLength + AuthTagLength)];
        var cipherText = wireBytes[(NonceLength + AuthTagLength)..];
        return (nonce, authTag, cipherText);
    }

    private static SharedLibraryFileSummary ToSummary(LibraryFileDto dto) =>
        new(dto.Id, dto.FolderPath, dto.FileName, dto.Tags, dto.SizeBytes, dto.UploadedByDeviceId.ToString(), dto.UploadedAtUtc);

    private sealed record LibraryFileDto(Guid Id, string FolderPath, string FileName, List<string> Tags, long SizeBytes, Guid UploadedByDeviceId, DateTimeOffset UploadedAtUtc);
}
