namespace SecureApp.Domain.Enums;

/// <summary>Structural or content-mutating actions gated by <see cref="Policies.RoleAccessPolicy"/>. Browsing/opening documents is never gated.</summary>
public enum RbacAction
{
    CreateFolder,
    RenameFolder,
    DeleteFolder,
    MoveFolder,
    ImportDocument,
    RenameDocument,
    DeleteDocument,
    MoveDocument,
    CreateChatSession,
    SendMessage,
    UploadLibraryFile,
    DeleteLibraryFile,

    /// <summary>Adding/removing a member on an existing group chat — the *founder* of a group can always do this regardless of role (see <c>GroupChat.FounderPublicKey</c>'s own remarks); this action gates it for everyone else, i.e. "admin or povereny uzivatel" (an authorized user) in the user's own framing — Modifier already represents exactly that tier in this app's 3-role model, so it's granted the same as every other RbacAction rather than inventing a 4th tier.</summary>
    InviteGroupMember,
    RemoveGroupMember
}
