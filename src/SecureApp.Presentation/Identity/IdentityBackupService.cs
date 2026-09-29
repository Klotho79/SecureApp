using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;

namespace SecureApp.Presentation.Identity;

/// <inheritdoc cref="IIdentityBackupService"/>
/// <remarks>
/// Lives in Presentation (needs <c>HttpClient</c> + file/share APIs), same established split every
/// other Http*Service here already follows. The passphrase-based key derivation (PBKDF2) is done with
/// plain <see cref="System.Security.Cryptography"/> rather than through <c>ICryptoService</c> — that
/// abstraction's job is the app's own vault-keyed crypto (ML-KEM/AES-GCM/ML-DSA), not one-off password
/// hashing for a disaster-recovery file; the actual AES-256-GCM sealing of the backup payload DOES go
/// through <c>ICryptoService.EncryptWithKeyAsync</c>/<c>DecryptWithKeyAsync</c>, the one place it
/// already supports a caller-supplied raw key instead of a vault-resolved one.
/// </remarks>
public sealed class IdentityBackupService : IIdentityBackupService
{
    private const int Pbkdf2Iterations = 210_000; // OWASP 2023 minimum recommendation for PBKDF2-SHA256
    private const int DerivedKeyLengthBytes = 32; // AES-256
    private const int SaltLengthBytes = 16;
    private static readonly JsonSerializerOptions HttpJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ICryptoService _crypto;
    private readonly IMessagingService _messaging;
    private readonly ICurrentUserService _currentUser;
    private readonly HttpClient _httpClient = new();

    public IdentityBackupService(ICryptoService crypto, IMessagingService messaging, ICurrentUserService currentUser)
    {
        _crypto = crypto ?? throw new ArgumentNullException(nameof(crypto));
        _messaging = messaging ?? throw new ArgumentNullException(nameof(messaging));
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
    }

    public async Task<string> BackupAsync(string email, string passphrase, CancellationToken ct = default)
    {
        var keyId = await _messaging.GetLocalIdentityKeyIdAsync(ct);
        var material = await _crypto.ExportEncryptionKeyMaterialAsync(keyId, ct);

        var payload = new BackupPayload(material.KeyId, material.PrivateKey, material.PublicKey, material.AesKey,
            _currentUser.Current.DisplayName, DateTimeOffset.UtcNow);
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, HttpJsonOptions);

        var salt = RandomNumberGenerator.GetBytes(SaltLengthBytes);
        var derivedKey = DeriveKey(passphrase, salt);
        var encrypted = await _crypto.EncryptWithKeyAsync(payloadBytes, material.KeyId, derivedKey, ct);

        var envelope = new BackupEnvelope(1, salt, encrypted.Nonce, encrypted.AuthTag, encrypted.CipherText);
        var envelopeJson = JsonSerializer.Serialize(envelope, HttpJsonOptions);

        var fileName = $"secureapp-identity-backup-{DateTime.Now:yyyy-MM-dd}.json";
        var filePath = Path.Combine(Microsoft.Maui.Storage.FileSystem.CacheDirectory, fileName);
        await File.WriteAllTextAsync(filePath, envelopeJson, ct);

        try
        {
            await Microsoft.Maui.ApplicationModel.DataTransfer.Share.Default.RequestAsync(
                new Microsoft.Maui.ApplicationModel.DataTransfer.ShareFileRequest
                {
                    Title = "Uložte zálohu identity mimo appku (Disk, e-mail, Soubory…)",
                    File = new Microsoft.Maui.ApplicationModel.DataTransfer.ShareFile(filePath),
                });
        }
        catch
        {
            // Best-effort — the file still exists at filePath even if the share sheet itself failed
            // (e.g. no share target available); the caller's own success message names that path.
        }

        try
        {
            var endpoint = await ResolveRelayHttpEndpointAsync(ct);
            var lookupKey = ComputeLookupKey(email, passphrase);
            using var request = new HttpRequestMessage(HttpMethod.Put, new Uri(endpoint, $"identity-backup/{lookupKey}"))
            {
                Content = JsonContent.Create(new IdentityBackupUploadDto(envelopeJson), options: HttpJsonOptions),
            };
            using var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
        }
        catch
        {
            // Best-effort — see this interface's own remarks: the local file is the primary
            // guarantee, the relay copy is a bonus that not being reachable right now shouldn't
            // block the backup the user is actually watching succeed or fail.
        }

        return filePath;
    }

    public async Task RestoreFromFileAsync(byte[] fileBytes, string passphrase, CancellationToken ct = default)
        => await RestoreFromEnvelopeJsonAsync(Encoding.UTF8.GetString(fileBytes), passphrase, ct);

    public async Task RestoreFromRelayAsync(Uri relayEndpoint, string email, string passphrase, CancellationToken ct = default)
    {
        var lookupKey = ComputeLookupKey(email, passphrase);
        var scheme = relayEndpoint.Scheme == "wss" ? "https" : "http";
        var httpBase = new UriBuilder(relayEndpoint) { Scheme = scheme, Port = relayEndpoint.Port, Path = "/" }.Uri;

        using var response = await _httpClient.GetAsync(new Uri(httpBase, $"identity-backup/{lookupKey}"), ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new InvalidOperationException("Na relay serveru není žádná záloha pro tenhle e-mail a heslo — zkontrolujte je, nebo obnovte ze souboru.");
        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<IdentityBackupUploadDto>(HttpJsonOptions, ct)
            ?? throw new InvalidOperationException("Relay vrátil prázdnou odpověď.");
        await RestoreFromEnvelopeJsonAsync(dto.EnvelopeJson, passphrase, ct);
    }

    private async Task RestoreFromEnvelopeJsonAsync(string envelopeJson, string passphrase, CancellationToken ct)
    {
        var envelope = JsonSerializer.Deserialize<BackupEnvelope>(envelopeJson, HttpJsonOptions)
            ?? throw new InvalidOperationException("Soubor zálohy se nepodařilo přečíst — není to platná záloha identity.");

        var derivedKey = DeriveKey(passphrase, envelope.Salt);
        var toDecrypt = new EncryptedPayload(Guid.Empty, Domain.Enums.EncryptionAlgorithm.Aes256Gcm, envelope.CipherText, envelope.Nonce, envelope.AuthTag);
        byte[] payloadBytes;
        try
        {
            payloadBytes = await _crypto.DecryptWithKeyAsync(toDecrypt, derivedKey, ct);
        }
        catch (Domain.Exceptions.EncryptionOperationException ex)
        {
            throw new InvalidOperationException("Špatné heslo, nebo je soubor zálohy poškozený.", ex);
        }

        var payload = JsonSerializer.Deserialize<BackupPayload>(payloadBytes, HttpJsonOptions)
            ?? throw new InvalidOperationException("Soubor zálohy se nepodařilo přečíst — není to platná záloha identity.");

        var material = new IdentityKeyMaterial(payload.KeyId, payload.PrivateKey, payload.PublicKey, payload.AesKey);
        await _messaging.RestoreLocalIdentityAsync(material, ct);

        if (!string.IsNullOrWhiteSpace(payload.DisplayName))
            await _currentUser.SetCurrentUserAsync(payload.DisplayName, _currentUser.Current.Role, ct);
    }

    private async Task<Uri> ResolveRelayHttpEndpointAsync(CancellationToken ct)
    {
        // Called only from BackupAsync, where the device is by definition already registered (an
        // unregistered device has no identity to back up yet) — so ITransportSettingsRepository always
        // has a real endpoint by this point, unlike the restore path, which takes the endpoint straight
        // from the Settings screen instead, since a fresh/wiped device has no repository entry yet.
        var services = Microsoft.Maui.IPlatformApplication.Current?.Services
            ?? throw new InvalidOperationException("Aplikace ještě není plně spuštěná.");
        var transportSettings = services.GetRequiredService<Domain.Interfaces.Repositories.ITransportSettingsRepository>();
        var configuration = await transportSettings.GetAsync(ct);
        var wsEndpoint = configuration?.EndpointUri ?? new Uri(Transport.RelayDefaults.DefaultEndpoint);
        var scheme = wsEndpoint.Scheme == "wss" ? "https" : "http";
        return new UriBuilder(wsEndpoint) { Scheme = scheme, Port = wsEndpoint.Port, Path = "/" }.Uri;
    }

    /// <summary>PBKDF2-SHA256, 210k iterations (OWASP 2023 baseline) — deliberately slow, this only ever runs once per backup/restore, never in a hot loop.</summary>
    private static byte[] DeriveKey(string passphrase, byte[] salt)
        => Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(passphrase), salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, DerivedKeyLengthBytes);

    /// <summary>
    /// The relay storage key for a backup — deliberately NOT the same derivation as <see cref="DeriveKey"/>
    /// (no per-backup salt, since the relay must be able to look this row up by recomputing the same
    /// value from email+passphrase alone, before it has ever seen this specific backup). This is the
    /// SAME "an unauthenticated relay endpoint is a known, accepted tradeoff for this small, trusted
    /// community" posture <c>/activation/request</c> already established — see Program.cs's own remarks
    /// there. The passphrase's own strength is what actually protects a backup, not this endpoint's shape.
    /// </summary>
    private static string ComputeLookupKey(string email, string passphrase)
    {
        var bytes = Encoding.UTF8.GetBytes(email.Trim().ToLowerInvariant() + "\u0000" + passphrase);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private sealed record BackupPayload(Guid KeyId, byte[] PrivateKey, byte[] PublicKey, byte[] AesKey, string DisplayName, DateTimeOffset CreatedAtUtc);
    private sealed record BackupEnvelope(int Version, byte[] Salt, byte[] Nonce, byte[] AuthTag, byte[] CipherText);
    private sealed record IdentityBackupUploadDto(string EnvelopeJson);
}
