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
    private static readonly JsonSerializerOptions HttpJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ITransportSettingsRepository _transportSettingsRepository;
    private readonly ISecureVaultKeyStore _vault;
    private readonly ICryptoService _crypto;
    private readonly HttpClient _httpClient = RelayHttp.CreateClient(TimeSpan.FromSeconds(30));

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
        var blob = Convert.ToBase64String(SharedKeyWire.Combine(payload.Nonce, payload.AuthTag, payload.CipherText));

        using var request = await RelayHttp.CreateAuthorizedRequestAsync(HttpMethod.Post, "board", _transportSettingsRepository, _vault, ct);
        request.Content = JsonContent.Create(new { ContentBlob = blob }, options: HttpJsonOptions);

        using var response = await _httpClient.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw new InvalidOperationException("Na nástěnku smí psát jen admin nebo pověřený uživatel (modifier).");
        response.EnsureSuccessStatusCode();
    }

    public async Task<bool> UpdateAsync(Guid postId, string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Zpráva na nástěnku nesmí být prázdná.");

        var key = await GetSharedKeyAsync(ct);
        var payload = await _crypto.EncryptWithKeyAsync(Encoding.UTF8.GetBytes(text), Guid.NewGuid(), key, ct);
        var blob = Convert.ToBase64String(SharedKeyWire.Combine(payload.Nonce, payload.AuthTag, payload.CipherText));

        using var request = await RelayHttp.CreateAuthorizedRequestAsync(HttpMethod.Put, $"board/{postId}", _transportSettingsRepository, _vault, ct);
        request.Content = JsonContent.Create(new { ContentBlob = blob }, options: HttpJsonOptions);

        using var response = await _httpClient.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw new InvalidOperationException("Příspěvky na nástěnce smí upravovat jen admin nebo pověřený uživatel (modifier).");
        if (response.StatusCode == HttpStatusCode.NotFound) return false;
        response.EnsureSuccessStatusCode();
        return true;
    }

    public async Task<IReadOnlyList<BoardPost>> ListAsync(int limit = 50, CancellationToken ct = default)
    {
        try
        {
            var key = await _vault.RetrieveSecretAsync(SharedLibraryKeyVaultKey, ct);
            using var request = await RelayHttp.CreateAuthorizedRequestAsync(HttpMethod.Get, $"board?limit={limit}", _transportSettingsRepository, _vault, ct);

            using var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var dtos = await response.Content.ReadFromJsonAsync<List<BoardPostDto>>(HttpJsonOptions, ct) ?? [];

            var result = new List<BoardPost>(dtos.Count);
            foreach (var d in dtos)
            {
                string text;
                bool isReadable;
                if (key is null)
                {
                    text = "🔒 (chybí klíč sdílené knihovny)";
                    isReadable = false;
                }
                else
                {
                    (text, isReadable) = await TryDecryptAsync(d.ContentBlob, key, ct);
                }
                result.Add(new BoardPost(d.Id, d.AuthorDeviceId, d.AuthorDisplayName, text, d.CreatedAtUtc, isReadable));
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
        using var request = await RelayHttp.CreateAuthorizedRequestAsync(HttpMethod.Delete, $"board/{postId}", _transportSettingsRepository, _vault, ct);

        using var response = await _httpClient.SendAsync(request, ct);
        return response.IsSuccessStatusCode;
    }

    private async Task<(string Text, bool IsReadable)> TryDecryptAsync(string blob, byte[] key, CancellationToken ct)
    {
        try
        {
            var wire = Convert.FromBase64String(blob);
            if (wire.Length < SharedKeyWire.NonceLength + SharedKeyWire.AuthTagLength) return ("⚠ (poškozená zpráva)", false);
            var (nonce, tag, cipher) = SharedKeyWire.Split(wire, "⚠ (poškozená zpráva)");
            var payload = new EncryptedPayload(Guid.Empty, EncryptionAlgorithm.Aes256Gcm, cipher, nonce, tag);
            var plaintext = await _crypto.DecryptWithKeyAsync(payload, key, ct);
            return (Encoding.UTF8.GetString(plaintext), true);
        }
        catch
        {
            // Wrong/stale local key for THIS post specifically (the common no-key-at-all case is
            // handled one level up in ListAsync, before this is even called) — still not readable.
            return ("🔒 (nešlo dešifrovat — zkontrolujte klíč sdílené knihovny)", false);
        }
    }

    private async Task<byte[]> GetSharedKeyAsync(CancellationToken ct)
        => await _vault.RetrieveSecretAsync(SharedLibraryKeyVaultKey, ct)
            ?? throw new InvalidOperationException("Nemáte klíč sdílené knihovny — nejprve ho v Nastavení vygenerujte nebo importujte.");

    private sealed record BoardPostDto(Guid Id, Guid AuthorDeviceId, string AuthorDisplayName, string ContentBlob, DateTimeOffset CreatedAtUtc);
}
