using SecureApp.Domain.ValueObjects;

namespace SecureApp.Presentation.Chat;

/// <summary>
/// Pure resolution logic behind the background identity-migration sweep (2026-09-19, the user's own
/// hard standing requirement — see the "secureapp-self-healing-requirement" memory: nobody should
/// ever have to notice or manually fix a broken chat/group member). When a device loses its local
/// identity (vault wipe, DB reset — see <c>TransportEndpointConfiguration</c>'s own remarks on
/// identity resets) it re-registers with the relay as an entirely NEW identity: new public key, new
/// RelayDeviceId. Nothing ever routes to the OLD identity again, no matter how many times a resync is
/// retried against it — the private key that could complete a handshake for it is gone for good. The
/// only signal that survives a reset is the peer's DISPLAY NAME (the user re-types it once in
/// Settings; nothing else does). This is what lets every OTHER device notice the same peer came back
/// under a new key and re-pair automatically, with no user action anywhere.
///
/// Orchestration (looping over sessions/groups, actually calling <see cref="SessionRecoveryHelper"/>)
/// lives in <c>App.xaml.cs</c>'s own background sweep, alongside <c>RunStaleSessionSweepAsync</c> —
/// this class only answers "does this stored peer look like it migrated, and to what."
/// </summary>
public static class PeerIdentityReconciler
{
    /// <summary>
    /// A stored peer (by <paramref name="storedDisplayName"/>/<paramref name="storedPublicKey"/>)
    /// looks like it migrated when: its OLD key is no longer anyone's active directory entry (an
    /// ordinary offline-but-still-real peer would still be there — the relay's own
    /// <c>DirectoryActiveWindow</c> already absorbs normal short outages, see
    /// <see cref="DirectoryNameResolver.IsActive"/>), AND exactly one CURRENTLY active directory
    /// member has the exact same display name under a DIFFERENT key. Deliberately returns null (does
    /// nothing) rather than guess when more than one active member shares that name — a wrong silent
    /// merge would be far worse than leaving a peer stale a little longer.
    /// </summary>
    public static DirectoryMember? TryResolveMigrationTarget(
        IReadOnlyList<DirectoryMember> directory, string storedDisplayName, byte[] storedPublicKey)
    {
        var storedKeyHex = Convert.ToHexStringLower(storedPublicKey);
        if (directory.Any(m => Convert.ToHexStringLower(m.PublicKey) == storedKeyHex))
            return null; // old key is still active — an ordinary (possibly offline) peer, not a migration

        var candidates = directory.Where(m => m.DisplayName == storedDisplayName).ToList();
        return candidates.Count == 1 ? candidates[0] : null;
    }
}
