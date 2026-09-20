using SecureApp.Domain.Common;

namespace SecureApp.Domain.Entities;

/// <summary>
/// One entry in the user's own, editable contact list (2026-09-20, user's own ask — distinct from
/// the static hospital phone directory <c>ContactDirectoryData</c> already shows). Two kinds, told
/// apart by <see cref="LinkedPublicKey"/>:
/// - Linked (not null): this contact IS a paired SecureApp community member — auto-created/kept in
///   sync from this device's own <c>ChatSession</c>s and <c>GroupMember</c> rows (see
///   <c>ContactsViewModel</c>'s own sync logic), so every paired peer always has a corresponding
///   entry with no manual step. Lets the UI offer a direct "open chat" action.
/// - Unlinked (null): a plain manually-added contact (e.g. someone not on SecureApp at all) — only
///   Phone/Email/Note apply, no in-app chat action.
///
/// <see cref="SortOrder"/> is purely a display-order hint the user controls by dragging (desktop
/// drag-and-drop, 2026-09-20's own ask) — never used for anything behavioral.
/// </summary>
public sealed class Contact : Entity
{
    public string DisplayName { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string? Note { get; private set; }
    public int SortOrder { get; private set; }
    public byte[]? LinkedPublicKey { get; private set; }
    public Guid? LinkedRelayDeviceId { get; private set; }

    private Contact()
    {
        // Reserved for materialization by persistence infrastructure.
        DisplayName = string.Empty;
    }

    public Contact(string displayName, int sortOrder, string? phone = null, string? email = null, string? note = null, byte[]? linkedPublicKey = null, Guid? linkedRelayDeviceId = null)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Display name cannot be empty.", nameof(displayName));

        DisplayName = displayName;
        Phone = phone;
        Email = email;
        Note = note;
        SortOrder = sortOrder;
        LinkedPublicKey = linkedPublicKey;
        LinkedRelayDeviceId = linkedRelayDeviceId;
    }

    public void Rename(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Display name cannot be empty.", nameof(displayName));
        DisplayName = displayName;
        Touch();
    }

    public void SetSortOrder(int sortOrder)
    {
        if (SortOrder == sortOrder) return;
        SortOrder = sortOrder;
        Touch();
    }

    /// <summary>Re-syncs the routing id a linked contact's peer republished with — a peer's RelayDeviceId can change (identity reset — see PeerIdentityReconciler) without their PublicKey changing here, since this row is keyed by name/PublicKey match at sync time, not by RelayDeviceId.</summary>
    public void UpdateLink(byte[] publicKey, Guid relayDeviceId)
    {
        LinkedPublicKey = publicKey;
        LinkedRelayDeviceId = relayDeviceId;
        Touch();
    }
}
