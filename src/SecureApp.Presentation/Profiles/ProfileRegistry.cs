using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SecureApp.Presentation.Profiles;

/// <summary>
/// 2026-10-05, user's own ask: several people share one Windows PC running SecureApp — each should
/// get their own persistent local identity/vault/chat-history via a simple username+password login,
/// like the existing Opicentrum credential fields. This registry lists who can log in; it deliberately
/// lives OUTSIDE any specific profile's own vault/DB (<see cref="ActiveProfile"/>'s own remarks), since
/// it has to be readable BEFORE a profile is chosen — a chicken-and-egg problem a per-profile store
/// can't solve.
///
/// The password here is a LOCAL ACCESS GATE ONLY, not a data-encryption key — matches the user's own
/// "jednoduše" (simply) framing, and avoids inventing a second password-derived-encryption scheme
/// alongside the one <see cref="Identity.IdentityBackupService"/> already has for a completely
/// different purpose (disaster-recovery backup). The actual per-profile data-at-rest protection comes
/// from the existing <c>ISecureVaultKeyStore</c>/SQLCipher mechanisms, namespaced per profile by
/// <see cref="ActiveProfile"/> — unchanged by what password a profile happens to have.
///
/// PBKDF2 parameters reuse <see cref="Identity.IdentityBackupService"/>'s own constants verbatim
/// (210,000 iterations, SHA-256, 32-byte derived output, 16-byte salt) purely for consistency across
/// the codebase's two password-hashing call sites — there is no cryptographic link between them.
/// </summary>
public static class ProfileRegistry
{
    private const int Pbkdf2Iterations = 210_000;
    private const int DerivedKeyLengthBytes = 32;
    private const int SaltLengthBytes = 16;

    public sealed record Profile(string Name, string FolderName, string PasswordHash, string PasswordSalt, bool IsOwner, DateTimeOffset CreatedAtUtc);

    private static string RegistryPath => Path.Combine(ActiveProfile.BaseDirectory, "profiles.json");

    public static IReadOnlyList<Profile> LoadAll()
    {
        try
        {
            if (!File.Exists(RegistryPath)) return [];
            var json = File.ReadAllText(RegistryPath);
            return JsonSerializer.Deserialize<List<Profile>>(json) ?? [];
        }
        catch
        {
            return []; // Best-effort — a corrupted/unreadable registry just shows no profiles, never crashes the picker.
        }
    }

    /// <summary>Sanitizes <paramref name="name"/> into a filesystem-safe folder name, de-duplicating against any existing profile's own folder.</summary>
    private static string MakeFolderName(string name, IReadOnlyList<Profile> existing)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim();
        if (sanitized.Length == 0) sanitized = "profile";

        var candidate = sanitized;
        var suffix = 1;
        while (existing.Any(p => string.Equals(p.FolderName, candidate, StringComparison.OrdinalIgnoreCase)))
            candidate = $"{sanitized}_{++suffix}";
        return candidate;
    }

    /// <summary>Creates a brand-new profile (name must not already exist). The very first profile ever created on this machine becomes the "owner" profile — see <see cref="Infrastructure.TrustedAdminDevices"/>'s own remarks on why that matters.</summary>
    public static Profile Create(string name, string password)
    {
        var existing = LoadAll();
        var salt = RandomNumberGenerator.GetBytes(SaltLengthBytes);
        var hash = DeriveHash(password, salt);
        var profile = new Profile(
            Name: name,
            FolderName: MakeFolderName(name, existing),
            PasswordHash: Convert.ToHexStringLower(hash),
            PasswordSalt: Convert.ToHexStringLower(salt),
            IsOwner: existing.Count == 0,
            CreatedAtUtc: DateTimeOffset.UtcNow);

        var updated = existing.Append(profile).ToList();
        Save(updated);
        return profile;
    }

    public static bool TryVerifyPassword(Profile profile, string password)
    {
        try
        {
            var salt = Convert.FromHexString(profile.PasswordSalt);
            var expected = Convert.FromHexString(profile.PasswordHash);
            var actual = DeriveHash(password, salt);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch
        {
            return false;
        }
    }

    private static byte[] DeriveHash(string password, byte[] salt)
        => Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, DerivedKeyLengthBytes);

    private static void Save(IReadOnlyList<Profile> profiles)
    {
        Directory.CreateDirectory(ActiveProfile.BaseDirectory);
        var json = JsonSerializer.Serialize(profiles);
        File.WriteAllText(RegistryPath, json);
    }
}
