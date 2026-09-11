using System.Text;

namespace SecureApp.Presentation.Chat;

/// <summary>
/// Wire format for a "delete this message" command (2026-09-11) — propagated over the same E2EE
/// ratchet as an ordinary message, tagged <c>Message.IsSystemPayload</c> so it never shows as a
/// bubble (see <c>SharedLibraryKeySync</c> for the same tagged-system-payload pattern). The command
/// carries only the message's cross-device correlation id (<c>Message.OriginMessageId</c>, or a group
/// message's <c>GroupMessageId</c>); the receiving side finds its own local copy by that id and
/// removes it (<c>IMessageRepository.DeleteByCorrelationAsync</c>).
///
/// The RBAC decision (<c>RoleAccessPolicy.CanDeleteMessage</c>) is made on the DELETER's device
/// before the command is ever sent — consistent with this app's existing cooperative role model,
/// where roles are self-declared in Settings and not server-enforced (the same trust already placed
/// in every other role-gated action). A recipient honors a delete command from a paired peer, the
/// same way it honors every other message that peer sends over the authenticated session.
/// </summary>
public static class MessageDeletionSync
{
    private const string DeletePayloadPrefix = "delete-message:v1:";

    public static byte[] BuildDeleteCommand(Guid correlationId) =>
        Encoding.UTF8.GetBytes(DeletePayloadPrefix + correlationId.ToString());

    public static bool TryParseDeleteCommand(byte[] plaintext, out Guid correlationId)
    {
        correlationId = Guid.Empty;
        string text;
        try { text = Encoding.UTF8.GetString(plaintext); }
        catch { return false; }

        if (!text.StartsWith(DeletePayloadPrefix, StringComparison.Ordinal))
            return false;

        return Guid.TryParse(text[DeletePayloadPrefix.Length..], out correlationId);
    }
}
