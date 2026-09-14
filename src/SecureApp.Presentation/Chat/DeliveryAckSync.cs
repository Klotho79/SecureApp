using System.Text;

namespace SecureApp.Presentation.Chat;

/// <summary>
/// Wire format for a delivery acknowledgment (2026-09-14, the user's ask: WhatsApp-style ✓/✓✓ where
/// the SECOND check means the recipient's APP confirmed it received the message — a delivery receipt,
/// NOT a read receipt). Sent back over the same E2EE ratchet as an ordinary message, tagged
/// <c>Message.IsSystemPayload</c> so it never shows as a bubble — the same tagged-system-payload
/// pattern as <see cref="MessageDeletionSync"/> and <c>SharedLibraryKeySync</c>, so no relay protocol
/// change is needed.
///
/// The ack carries only the original message's cross-device correlation id
/// (<c>Message.OriginMessageId</c>, or a group message's <c>GroupMessageId</c>); the original sender
/// finds its own local copy by that id and marks it Delivered. An ack is itself a system payload and is
/// never acked back, so there is no ack loop.
/// </summary>
public static class DeliveryAckSync
{
    private const string AckPayloadPrefix = "delivery-ack:v1:";

    public static byte[] BuildAck(Guid correlationId) =>
        Encoding.UTF8.GetBytes(AckPayloadPrefix + correlationId.ToString());

    public static bool TryParseAck(byte[] plaintext, out Guid correlationId)
    {
        correlationId = Guid.Empty;
        string text;
        try { text = Encoding.UTF8.GetString(plaintext); }
        catch { return false; }

        if (!text.StartsWith(AckPayloadPrefix, StringComparison.Ordinal))
            return false;

        return Guid.TryParse(text[AckPayloadPrefix.Length..], out correlationId);
    }
}
