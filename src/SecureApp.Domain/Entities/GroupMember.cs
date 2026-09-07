using SecureApp.Domain.Common;

namespace SecureApp.Domain.Entities;

/// <summary>
/// One member row of a <see cref="GroupChat"/> — including a row for the local device itself
/// (identifiable by comparing <see cref="PublicKey"/> against <c>IMessagingService.GetLocalIdentityPublicKeyAsync()</c>
/// at runtime), so a full member list is always self-contained without a separate "and also me"
/// special case anywhere that reads it.
///
/// Deliberately carries NO cached <c>ChatSessionId</c> — the pairwise <see cref="ChatSession"/> to
/// use for this member is always resolved on demand via <c>IMessagingService.FindExistingSessionAsync(PublicKey)</c>
/// when actually sending. A cached id would need active invalidation if that session were ever
/// replaced/re-paired; looking it up fresh each time is one cheap local query and can never go
/// stale.
/// </summary>
public sealed class GroupMember : Entity
{
    public Guid GroupChatId { get; private set; }
    public string DisplayName { get; private set; }
    public byte[] PublicKey { get; private set; }
    public Guid RelayDeviceId { get; private set; }

    /// <summary>Whether this member (other than the founder, who can always invite/remove — see <c>GroupChat.FounderPublicKey</c>) is allowed to invite/remove other members. Defaults false; only a founder or an existing Admin/Modifier-role device can grant it — see <c>RbacAction.InviteGroupMember</c>'s own remarks. No dedicated UI to toggle this yet in this pass (deferred), but the data model supports it so that's additive later, not a schema change.</summary>
    public bool CanInvite { get; private set; }

    private GroupMember()
    {
        // Reserved for materialization by persistence infrastructure.
        DisplayName = string.Empty;
        PublicKey = Array.Empty<byte>();
    }

    public GroupMember(Guid groupChatId, string displayName, byte[] publicKey, Guid relayDeviceId, bool canInvite = false)
    {
        if (groupChatId == Guid.Empty)
            throw new ArgumentException("Group chat id cannot be empty.", nameof(groupChatId));
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Display name cannot be empty.", nameof(displayName));
        if (publicKey is null || publicKey.Length == 0)
            throw new ArgumentException("Public key cannot be empty.", nameof(publicKey));
        if (relayDeviceId == Guid.Empty)
            throw new ArgumentException("Relay device id cannot be empty.", nameof(relayDeviceId));

        GroupChatId = groupChatId;
        DisplayName = displayName;
        PublicKey = publicKey;
        RelayDeviceId = relayDeviceId;
        CanInvite = canInvite;
    }

    public void SetCanInvite(bool canInvite)
    {
        CanInvite = canInvite;
        Touch();
    }
}
