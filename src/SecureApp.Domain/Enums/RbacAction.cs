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
    DeleteLibraryFile
}
