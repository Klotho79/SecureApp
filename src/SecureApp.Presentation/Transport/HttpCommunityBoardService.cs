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
public sealed class HttpCommunityBoardService : RelayServiceBase, ICommunityBoardService
{
    private const string SharedLibraryKeyVaultKey = "library:shared-key";

    private readonly ICryptoService _crypto;

    public HttpCommunityBoardService(ITransportSettingsRepository transportSettingsRepository, ISecureVaultKeyStore vault, ICryptoService crypto)
        : base(vault, transportSettingsRepository, TimeSpan.FromSeconds(30))
    {
        _crypto = crypto ?? throw new ArgumentNullException(nameof(crypto));
    }

    public async Task PostAsync(string text, CancellationToken ct = default)
    {
        using var request = await CreateRequestAsync(HttpMethod.Post, "board", new { ContentBlob = await EncryptAsync(text, ct) }, ct);
        using var response = await Http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw new InvalidOperationException("Na nástěnku smí psát jen admin nebo pověřený uživatel (modifier).");
        response.EnsureSuccessStatusCode();
    }

    public async Task<bool> UpdateAsync(Guid postId, string text, CancellationToken ct = default)
    {
        using var request = await CreateRequestAsync(HttpMethod.Put, $"board/{postId}", new { ContentBlob = await EncryptAsync(text, ct) }, ct);
        using var response = await Http.SendAsync(request, ct);
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
            var key = await Vault.RetrieveSecretAsync(SharedLibraryKeyVaultKey, ct);
            var dtos = await GetJsonAsync<List<BoardPostDto>>($"board?limit={limit}", ct) ?? [];

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
        using var request = await CreateRequestAsync(HttpMethod.Delete, $"board/{postId}", null, ct);
        using var response = await Http.SendAsync(request, ct);
        return response.IsSuccessStatusCode;
    }

    /// <summary>The post text encrypted with the shared community key, as the base64 wire blob. Rejects an empty text.</summary>
    private async Task<string> EncryptAsync(string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Zpráva na nástěnku nesmí být prázdná.");

        var key = await GetSharedKeyAsync(ct);
        var payload = await _crypto.EncryptWithKeyAsync(Encoding.UTF8.GetBytes(text), Guid.NewGuid(), key, ct);
        return Convert.ToBase64String(SharedKeyWire.Combine(payload.Nonce, payload.AuthTag, payload.CipherText));
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
        => await Vault.RetrieveSecretAsync(SharedLibraryKeyVaultKey, ct)
            ?? throw new InvalidOperationException("Nemáte klíč sdílené knihovny — nejprve ho v Nastavení vygenerujte nebo importujte.");

    private sealed record BoardPostDto(Guid Id, Guid AuthorDeviceId, string AuthorDisplayName, string ContentBlob, DateTimeOffset CreatedAtUtc);
}
