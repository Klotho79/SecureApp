using System.Collections.Concurrent;
using Microsoft.Maui.Storage;
using SecureApp.Domain.Interfaces.Services;

namespace SecureApp.Presentation.Infrastructure;

/// <summary>
/// <see cref="ISecureVaultKeyStore"/> backed by .NET MAUI's cross-platform
/// <see cref="ISecureStorage"/> — Android Keystore, iOS/macOS Keychain, Windows
/// DPAPI/Credential Locker under the hood, exactly the three mechanisms this
/// contract's own doc comment names. <see cref="ISecureStorage"/>'s underlying API is
/// string-only, so raw secret bytes are base64-encoded going in and decoded coming
/// back out; MAUI itself owns the platform-specific encryption at rest.
///
/// In-memory cache (2026-09-11, measured): a platform secure-storage read is expensive —
/// on the Android Keystore it is ~tens of milliseconds per call. Because
/// <c>ICryptoService.DecryptAsync</c> fetches the message/document AES key here on EVERY
/// decrypt, and a whole chat page shares ONE such key, opening a group chat was doing ~20
/// identical Keystore reads back to back (~800 ms total) — the real cause of the slow open.
/// This process-lifetime cache serves repeat reads of the same secret from memory. That is
/// consistent with the app's existing posture: the SQLCipher database key is already held
/// open in memory for the whole session and decrypted plaintext lives in memory while in use;
/// the platform vault's job is protection AT REST, which is unchanged. The cache is coherent
/// with writes because every write goes through <see cref="StoreSecretAsync"/>/<see cref="RemoveSecretAsync"/>
/// below (this store is a singleton and the only writer to its own keys).
/// </summary>
public sealed class MauiSecureVaultKeyStore : ISecureVaultKeyStore
{
    private readonly ISecureStorage _secureStorage;
    private readonly string _keyPrefix;
    private readonly ConcurrentDictionary<string, byte[]?> _cache = new();

    /// <summary>
    /// <paramref name="keyPrefix"/> namespaces every key before it reaches <see cref="ISecureStorage"/>
    /// — normally empty (single real identity per device). Exists so two instances of this same
    /// built app can be launched side-by-side on one machine with distinct identities for manual
    /// testing (see <c>SECUREAPP_DATA_DIR</c> in <c>MauiProgram.cs</c>) without needing to know or
    /// rely on whether the platform's real <see cref="ISecureStorage"/> happens to already be
    /// isolated per launched instance — it deliberately isn't assumed either way.
    /// </summary>
    public MauiSecureVaultKeyStore(ISecureStorage secureStorage, string keyPrefix = "")
    {
        _secureStorage = secureStorage ?? throw new ArgumentNullException(nameof(secureStorage));
        _keyPrefix = keyPrefix;
    }

    public async Task StoreSecretAsync(string key, ReadOnlyMemory<byte> secret, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var prefixed = Prefixed(key);
        await _secureStorage.SetAsync(prefixed, Convert.ToBase64String(secret.Span));
        _cache[prefixed] = secret.ToArray(); // keep the cache coherent with the write
    }

    public async Task<byte[]?> RetrieveSecretAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var prefixed = Prefixed(key);
        if (_cache.TryGetValue(prefixed, out var cached))
            return cached; // memory hit — avoids a ~tens-of-ms platform Keystore/Keychain read

        var base64 = await _secureStorage.GetAsync(prefixed);
        var value = base64 is null ? null : Convert.FromBase64String(base64);
        _cache[prefixed] = value; // cache hits AND misses; a later StoreSecretAsync updates a miss
        return value;
    }

    public Task RemoveSecretAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var prefixed = Prefixed(key);
        _secureStorage.Remove(prefixed);
        _cache[prefixed] = null; // reflect the removal as a cached miss
        return Task.CompletedTask;
    }

    private string Prefixed(string key) => _keyPrefix.Length == 0 ? key : $"{_keyPrefix}:{key}";
}
