using System.Text.Json;
using Microsoft.Maui.Storage;

namespace SecureApp.Presentation.Chat;

/// <summary>One pairing invite from a REMOVED peer, held for the user's explicit consent (2026-09-14, 2.2).</summary>
/// <param name="InitiatorDisplayName">Shown in the consent prompt.</param>
/// <param name="InitiatorPublicKeyHex">Lower-hex identity key — clears the removal in <see cref="RemovedPeersStore"/> on accept, and de-dupes repeated invites.</param>
/// <param name="InviteBlob">The raw pairing-invite blob (as received) so accepting can complete the handshake later.</param>
/// <param name="ReceivedAtUtc">When it arrived.</param>
public sealed record PendingInvite(string InitiatorDisplayName, string InitiatorPublicKeyHex, string InviteBlob, DateTimeOffset ReceivedAtUtc);

/// <summary>
/// Per-device queue of pairing invites from peers the user previously REMOVED (see
/// <see cref="RemovedPeersStore"/>) — held instead of auto-accepted, so a removed chat returns ONLY
/// when its creator re-invites AND the user consents (2.2, the user's policy). Surfaced as a banner in
/// the chat list; accepting clears the removal and completes the handshake, declining discards it and
/// keeps the peer removed. Preferences-backed (JSON) for the same DI-free, no-migration reasons as
/// <see cref="RemovedPeersStore"/>; the invite blob is the peer's own already-encrypted handshake, not a
/// local secret.
/// </summary>
public static class PendingInvitesStore
{
    private const string Key = "pending_invites_v1";
    private static readonly object _gate = new();

    public static IReadOnlyList<PendingInvite> GetAll()
    {
        try
        {
            var raw = Preferences.Default.Get(Key, string.Empty);
            if (string.IsNullOrEmpty(raw)) return [];
            return JsonSerializer.Deserialize<List<PendingInvite>>(raw) ?? [];
        }
        catch { return []; }
    }

    /// <summary>Adds a pending invite, replacing any earlier one from the same peer (only the latest handshake is worth keeping).</summary>
    public static void Add(PendingInvite invite)
    {
        lock (_gate)
        {
            var list = GetAll().Where(i => i.InitiatorPublicKeyHex != invite.InitiatorPublicKeyHex).ToList();
            list.Add(invite);
            Save(list);
        }
    }

    public static void Remove(string initiatorPublicKeyHex)
    {
        lock (_gate)
        {
            var list = GetAll().Where(i => i.InitiatorPublicKeyHex != initiatorPublicKeyHex).ToList();
            Save(list);
        }
    }

    public static bool HasAny() => GetAll().Count > 0;

    private static void Save(List<PendingInvite> list)
    {
        try { Preferences.Default.Set(Key, JsonSerializer.Serialize(list)); }
        catch { /* best-effort */ }
    }
}
