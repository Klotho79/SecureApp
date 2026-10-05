using Microsoft.Maui.Storage;

namespace SecureApp.Presentation.Chat;

/// <summary>
/// Per-device record of peers the user has explicitly REMOVED (deleted the chat with) — originally
/// (2026-09-14) also consulted by every automatic session-recreate path (the stale-session sweep, the
/// pairing auto-accept) to hold a removed peer's fresh re-invite for explicit consent instead of
/// silently resurrecting the chat.
///
/// 2026-10-04 — that consent gate is REMOVED (see <c>App.OnPairingInviteReceived</c>'s own remarks):
/// the user explicitly reversed the 2026-09-14 policy once it caused a real, found-live bug (a
/// removed peer's re-invite silently waiting, unseen, for 16+ hours) — this is a closed,
/// personally-vetted community, so there is no scenario where re-pairing with someone already in it
/// needs a second manual gate. This store still exists and is still populated on delete (see
/// <c>ChatListViewModel.DeleteSessionAsync</c>), since group-member filtering elsewhere still reads
/// it (see <c>GroupChatViewModel</c>'s own remarks) — but it no longer blocks anything about 1:1
/// pairing itself.
///
/// Backed by MAUI <see cref="Preferences"/> rather than the SQLCipher DB: it's a small per-device set of
/// PUBLIC keys (nothing secret), needs no schema migration, and must be readable from the same static,
/// DI-free spots (App's background handlers) that already can't easily reach a repository. Stored as a
/// ';'-separated list of lower-hex identity public keys.
/// </summary>
public static class RemovedPeersStore
{
    // 2026-10-05 — Windows multi-profile login: prefixed per-profile, see ArchivedChatsStore's own
    // identical remarks and Profiles.ActiveProfile.
    private static string Key => Profiles.ActiveProfile.PrefKey("removed_peers_v1");

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

    /// <summary>Clears the removal — call when the user starts a new chat with this peer, or (since 2026-10-04) any fresh pairing invite from them is received, since 1:1 re-pairing is no longer gated on this at all.</summary>
    public static void Remove(byte[] peerPublicKey)
    {
        var set = Load();
        if (set.Remove(Hex(peerPublicKey))) Save(set);
    }

    /// <summary>True if the user has removed (deleted the chat with) this peer — consulted by group-member filtering only; 1:1 pairing no longer checks this (see this class' own remarks).</summary>
    public static bool Contains(byte[] peerPublicKey) => Load().Contains(Hex(peerPublicKey));
}
