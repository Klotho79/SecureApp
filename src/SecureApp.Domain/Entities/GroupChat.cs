using SecureApp.Domain.Common;

namespace SecureApp.Domain.Entities;

/// <summary>
/// A group chat's header row (2026-09-07) — member rows live separately, see <see cref="GroupMember"/>.
///
/// Crypto design, stated explicitly since it's a real architectural choice: this deliberately does
/// NOT invent a new "Sender Keys"/MLS-style group ratchet, despite <c>ChatSession</c>'s own remarks
/// once flagging that as the assumed direction. Instead a group message fans out over the SAME
/// already-proven pairwise Double Ratchet — every pair of members maintains their own ordinary
/// <see cref="ChatSession"/> (a full mesh, not just founder-to-member), and sending to the group
/// just means encrypting once per other member's existing session (see
/// <c>IMessagingService.SendMessageAsync</c>'s new optional group-tagging parameters). Trade-off,
/// named directly: O(n) work per group message instead of Sender Keys' O(1), and O(n²) total
/// pairwise sessions across a group instead of O(n) — both entirely reasonable for a small,
/// admin-curated team (the actual target user), and it reuses 100% already-tested crypto rather
/// than a new hand-rolled ratchet construction, which is where real vulnerabilities tend to get
/// introduced. A founder-mediated design (only the founder holds pairwise sessions, relaying
/// everyone else's traffic) was considered and rejected: it would make ordinary group function
/// depend on the founder's device being reachable, a materially worse resilience property for a
/// clinical team than the extra pairwise sessions cost.
///
/// <see cref="Id"/> is NOT locally generated per device the way <see cref="ChatSession.Id"/> is —
/// the founder mints it once and every member's local row uses that SAME value, propagated via the
/// group-invite blob (see <c>IMessageTransport.SendGroupInviteAsync</c>'s remarks), specifically so
/// a received group message's <c>GroupChatId</c> tag needs no per-recipient correlation step the
/// way <c>MessageEnvelope.SessionId</c> does.
/// </summary>
public sealed class GroupChat : Entity
{
    public string Name { get; private set; }

    /// <summary>Identifies the founder across every member's own copy of this group — compared against <c>IMessagingService.GetLocalIdentityPublicKeyAsync()</c> at runtime to answer "am I the founder", never persisted as a separate bool.</summary>
    public byte[] FounderPublicKey { get; private set; }

    private GroupChat()
    {
        // Reserved for materialization by persistence infrastructure.
        Name = string.Empty;
        FounderPublicKey = Array.Empty<byte>();
    }

    /// <summary>
    /// <paramref name="id"/> is required (not auto-generated) — see the class-level remarks on why
    /// every member's local row must share the exact same id.
    /// </summary>
    public GroupChat(Guid id, string name, byte[] founderPublicKey) : base(id)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Group name cannot be empty.", nameof(name));
        if (founderPublicKey is null || founderPublicKey.Length == 0)
            throw new ArgumentException("Founder public key cannot be empty.", nameof(founderPublicKey));

        Name = name;
        FounderPublicKey = founderPublicKey;
    }

    /// <summary>Renaming propagates to other members the same way membership changes do — a fresh group-invite snapshot broadcast, not a dedicated wire message. Kept here rather than made immutable in case that's worth adding later; nothing in this pass calls it yet.</summary>
    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Group name cannot be empty.", nameof(name));

        Name = name;
        Touch();
    }
}
