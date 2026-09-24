using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;

namespace SecureApp.Presentation.Transport;

/// <inheritdoc cref="ICommunityBoardService"/>
/// <remarks>
/// Board messages are encrypted client-side with the SAME shared community key the file library
/// uses (vault key <c>library:shared-key</c>) — so the relay only ever stores ciphertext, and any
/// member who can open a library file can also read the board, with no extra key to distribute. The
/// on-wire blob is <c>Nonce(12)+AuthTag(16)+CipherText</c> base64'd, matching
/// <c>HttpSharedLibraryService</c>'s own format.
/// </remarks>
public sealed class HttpCommunityBoardService : ICommunityBoardService
{
    private const string SharedLibraryKeyVaultKey = "library:shared-key";
    private const int NonceLength = 12;
    private const int AuthTagLength = 16;
    private static readonly JsonSerializerOptions HttpJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ITransportSettingsRepository _transportSettingsRepository;
    private readonly ISecureVaultKeyStore _vault;
    private readonly ICryptoService _crypto;
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    public HttpCommunityBoardService(ITransportSettingsRepository transportSettingsRepository, ISecureVaultKeyStore vault, ICryptoService crypto)
    {
        _transportSettingsRepository = transportSettingsRepository ?? throw new ArgumentNullException(nameof(transportSettingsRepository));
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _crypto = crypto ?? throw new ArgumentNullException(nameof(crypto));
    }

    public async Task PostAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Zpráva na nástěnku nesmí být prázdná.");

        var key = await GetSharedKeyAsync(ct);
        var payload = await _crypto.EncryptWithKeyAsync(Encoding.UTF8.GetBytes(text), Guid.NewGuid(), key, ct);
        var blob = Convert.ToBase64String(Combine(payload.Nonce, payload.AuthTag, payload.CipherText));

        var (baseUri, deviceId, secret) = await GetAuthAsync(ct);
        var uri = new Uri(baseUri, "board");
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = JsonContent.Create(new { ContentBlob = blob }, options: HttpJsonOptions)
        };
        AddAuth(request, deviceId, secret);

        using var response = await _httpClient.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw new InvalidOperationException("Na nástěnku smí psát jen admin nebo pověřený uživatel (modifier).");
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<BoardPost>> ListAsync(int limit = 50, CancellationToken ct = default)
    {
        try
        {
            var key = await _vault.RetrieveSecretAsync(SharedLibraryKeyVaultKey, ct);
            var (baseUri, deviceId, secret) = await GetAuthAsync(ct);
            var uri = new Uri(baseUri, $"board?limit={limit}");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            AddAuth(request, deviceId, secret);

            using var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var dtos = await response.Content.ReadFromJsonAsync<List<BoardPostDto>>(HttpJsonOptions, ct) ?? [];

            var result = new List<BoardPost>(dtos.Count);
            foreach (var d in dtos)
            {
                var text = key is null ? "🔒 (chybí klíč sdílené knihovny)" : await TryDecryptAsync(d.ContentBlob, key, ct);
                result.Add(new BoardPost(d.Id, d.AuthorDeviceId, d.AuthorDisplayName, text, d.CreatedAtUtc));
            }
            return result;
        }
        catch
        {
            return []; // best-effort per the interface contract
        }
    }

    public async Task<bool> DeleteAsync(Guid postId, CancellationToken ct = default)
    {
        var (baseUri, deviceId, secret) = await GetAuthAsync(ct);
        var uri = new Uri(baseUri, $"board/{postId}");
        using var request = new HttpRequestMessage(HttpMethod.Delete, uri);
        AddAuth(request, deviceId, secret);

        using var response = await _httpClient.SendAsync(request, ct);
        return response.IsSuccessStatusCode;
    }

    private async Task<string> TryDecryptAsync(string blob, byte[] key, CancellationToken ct)
    {
        try
        {
            var wire = Convert.FromBase64String(blob);
            if (wire.Length < NonceLength + AuthTagLength) return "⚠ (poškozená zpráva)";
            var nonce = wire[..NonceLength];
            var tag = wire[NonceLength..(NonceLength + AuthTagLength)];
            var cipher = wire[(NonceLength + AuthTagLength)..];
            var payload = new EncryptedPayload(Guid.Empty, EncryptionAlgorithm.Aes256Gcm, cipher, nonce, tag);
            var plaintext = await _crypto.DecryptWithKeyAsync(payload, key, ct);
            return Encoding.UTF8.GetString(plaintext);
        }
        catch
        {
            return "🔒 (nešlo dešifrovat — zkontrolujte klíč sdílené knihovny)";
        }
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

        var scheme = wsEndpoint.Scheme switch { "ws" => "http", "wss" => "https", _ => wsEndpoint.Scheme };
        var baseUri = new UriBuilder(wsEndpoint) { Scheme = scheme, Port = wsEndpoint.Port }.Uri;
        return (baseUri, deviceId, Encoding.UTF8.GetString(secretBytes));
    }

    private static void AddAuth(HttpRequestMessage request, Guid deviceId, string secret)
    {
        request.Headers.Add("X-Device-Id", deviceId.ToString());
        request.Headers.Add("X-Device-Secret", secret);
    }

    private static byte[] Combine(byte[] nonce, byte[] authTag, byte[] cipherText)
    {
        var result = new byte[nonce.Length + authTag.Length + cipherText.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, nonce.Length);
        Buffer.BlockCopy(authTag, 0, result, nonce.Length, authTag.Length);
        Buffer.BlockCopy(cipherText, 0, result, nonce.Length + authTag.Length, cipherText.Length);
        return result;
    }

    private sealed record BoardPostDto(Guid Id, Guid AuthorDeviceId, string AuthorDisplayName, string ContentBlob, DateTimeOffset CreatedAtUtc);
}
