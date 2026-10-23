using System.Text.Json;
using Microsoft.Maui.Storage;

namespace SecureApp.Presentation.Chat;

/// <summary>
/// Pairing invites that were created but could not be handed to the relay (2026-10-23). A resync made
/// while the relay was unreachable used to create the new session, log "invite-queued" and forget the
/// invite: the initiator's session counts as Active from the start, so nothing ever looked at it again
/// and the peer never learned a new session existed — every message either side then sent failed to
/// decrypt until some unrelated trigger happened to resync. The supervisor now re-sends whatever is
/// stored here as soon as the relay is connected (<see cref="SessionRecoveryHelper.FlushPendingInvitesAsync"/>).
///
/// Same storage choice as <see cref="RemovedPeersStore"/>: MAUI <see cref="Preferences"/>, per profile.
/// The blob is the public invite (identity public key + handshake ciphertext), nothing secret. One entry
/// per peer — a newer invite for the same peer replaces the older, which no longer matches any session.
/// </summary>
public static class PendingInviteStore
{
    public sealed record Entry(Guid SessionId, Guid RelayDeviceId, string Blob);

    private static readonly object Gate = new();
    private static string Key => Profiles.ActiveProfile.PrefKey("pending_invites_v1");

    private static Dictionary<string, Entry> Load()
    {
        try
        {
            var raw = Preferences.Default.Get(Key, string.Empty);
            return string.IsNullOrEmpty(raw)
                ? new Dictionary<string, Entry>(StringComparer.Ordinal)
                : JsonSerializer.Deserialize<Dictionary<string, Entry>>(raw) ?? new Dictionary<string, Entry>(StringComparer.Ordinal);
        }
        catch
        {
            return new Dictionary<string, Entry>(StringComparer.Ordinal);
        }
    }

    private static void Save(Dictionary<string, Entry> entries)
    {
        try { Preferences.Default.Set(Key, JsonSerializer.Serialize(entries)); }
        catch { /* best-effort — worst case the peer is re-invited by the next resync */ }
    }

    private static string Hex(byte[] peerPublicKey) => Convert.ToHexStringLower(peerPublicKey);

    public static void Set(byte[] peerPublicKey, Guid sessionId, Guid relayDeviceId, string blob)
    {
        lock (Gate)
        {
            var entries = Load();
            entries[Hex(peerPublicKey)] = new Entry(sessionId, relayDeviceId, blob);
            Save(entries);
        }
    }

    public static void Remove(byte[] peerPublicKey)
    {
        lock (Gate)
        {
            var entries = Load();
            if (entries.Remove(Hex(peerPublicKey))) Save(entries);
        }
    }

    public static IReadOnlyList<(string PeerKeyHex, Entry Entry)> All()
    {
        lock (Gate)
            return Load().Select(kv => (kv.Key, kv.Value)).ToList();
    }

    public static void RemoveByHex(string peerKeyHex)
    {
        lock (Gate)
        {
            var entries = Load();
            if (entries.Remove(peerKeyHex)) Save(entries);
        }
    }
}
