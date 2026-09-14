using Microsoft.Maui.Storage;

namespace SecureApp.Presentation.Chat;

/// <summary>
/// Per-device record of peers the user has explicitly REMOVED (deleted the chat with) — 2026-09-14,
/// the user's policy: "když si uživatel odebere nějaký chat, už by se mu neměl automaticky obnovit, jen
/// když ho ten kdo chat vytvořil znovu pozve a uživatel s tím souhlasí." Every automatic
/// session-recreate path (the stale-session sweep, the decrypt-failure auto-heal, the pairing
/// auto-accept) consults this so a removed peer is NOT silently resurrected; a fresh pairing invite from
/// a removed peer becomes a consent prompt instead (see PendingInvitesStore), and only accepting it (or
/// the user starting a new chat with them) clears the removal.
///
/// Backed by MAUI <see cref="Preferences"/> rather than the SQLCipher DB: it's a small per-device set of
/// PUBLIC keys (nothing secret), needs no schema migration, and must be readable from the same static,
/// DI-free spots (App's background handlers) that already can't easily reach a repository. Stored as a
/// ';'-separated list of lower-hex identity public keys.
/// </summary>
public static class RemovedPeersStore
{
    private const string Key = "removed_peers_v1";

    private static HashSet<string> Load()
    {
        try
        {
            var raw = Preferences.Default.Get(Key, string.Empty);
            return string.IsNullOrEmpty(raw)
                ? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(raw.Split(';', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
        }
        catch
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    private static void Save(HashSet<string> set)
    {
        try { Preferences.Default.Set(Key, string.Join(';', set)); }
        catch { /* best-effort — a lost write just means a removed peer might re-appear once, not a crash */ }
    }

    private static string Hex(byte[] peerPublicKey) => Convert.ToHexStringLower(peerPublicKey);

    /// <summary>Marks a peer as removed by the user — call when a chat is deleted.</summary>
    public static void Add(byte[] peerPublicKey)
    {
        var set = Load();
        if (set.Add(Hex(peerPublicKey))) Save(set);
    }

    /// <summary>Clears the removal — call when the user starts a new chat with, or accepts a re-invite from, this peer.</summary>
    public static void Remove(byte[] peerPublicKey)
    {
        var set = Load();
        if (set.Remove(Hex(peerPublicKey))) Save(set);
    }

    /// <summary>True if the user has removed this peer and it must not be silently re-paired.</summary>
    public static bool Contains(byte[] peerPublicKey) => Load().Contains(Hex(peerPublicKey));
}
