namespace SecureApp.Presentation.Chat;

/// <summary>One member entry inside a <see cref="GroupInviteBlob"/> — the same three fields a <see cref="ContactCardBlob"/> carries.</summary>
public sealed record GroupMemberBlob(string DisplayName, byte[] PublicKey, Guid RelayDeviceId);

/// <summary>
/// A full group-membership snapshot (2026-09-07) — sent via <c>IMessageTransport.SendGroupInviteAsync</c>
/// whenever a group is created, renamed, or its membership changes (always the WHOLE list, never an
/// incremental diff — see <c>IGroupMemberRepository.ReplaceAllAsync</c>'s own remarks). Includes the
/// founder as an ordinary entry in <see cref="Members"/> too, so the blob is self-contained: a
/// recipient never needs a separate lookup to know who's in the group besides themselves.
/// </summary>
public sealed record GroupInviteBlob(Guid GroupId, string GroupName, byte[] FounderPublicKey, IReadOnlyList<GroupMemberBlob> Members);
