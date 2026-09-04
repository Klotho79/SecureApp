namespace SecureApp.Domain.Enums;

/// <summary>Security-relevant events recorded to the append-only audit trail.</summary>
public enum AuditAction
{
    VaultUnlocked,
    VaultLockFailed,
    DocumentImported,
    DocumentViewed,
    DocumentUpdated,
    DocumentDeleted,
    DocumentExported,
    SpreadsheetParsed,
    EncryptionKeyGenerated,
    EncryptionKeyRotated,
    ChatSessionEstablished,
    ChatSessionClosed,
    MessageSent,
    MessageReceived
}
