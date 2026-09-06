namespace SecureApp.Domain.ValueObjects;

/// <summary>
/// One other community member already known to the relay's member directory (2026-09-06) — see
/// <c>IContactDirectoryService</c>'s own remarks. Carries everything <c>NewChatViewModel</c> needs
/// to start a chat session directly (same three fields a manually pasted <c>ContactCardBlob</c>
/// would carry), with no QR/copy-paste step at all.
/// </summary>
public sealed record DirectoryMember(Guid RelayDeviceId, string DisplayName, byte[] PublicKey);
