namespace SecureApp.Domain.ValueObjects;

/// <summary>
/// One other community member already known to the relay's member directory (2026-09-06) — see
/// <c>IContactDirectoryService</c>'s own remarks. Carries everything <c>NewChatViewModel</c> needs
/// to start a chat session directly (same three fields a manually pasted <c>ContactCardBlob</c>
/// would carry), with no QR/copy-paste step at all.
/// </summary>
/// <param name="FormalName">2026-10-08 — Jméno Příjmení, published alongside DisplayName (see <c>User</c>'s own remarks) so a chat UI can reveal it on long-press without touching the QR/contact-card handshake. Null for a peer on an older build, or one who never filled in Jméno/Příjmení.</param>
public sealed record DirectoryMember(Guid RelayDeviceId, string DisplayName, byte[] PublicKey, string? FormalName = null);
