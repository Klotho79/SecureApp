using System.Text;

namespace SecureApp.Presentation.Chat;

/// <summary>
/// A second, QR-specific encoding for <see cref="ContactCardBlob"/>/<see cref="ChatInviteBlob"/> —
/// exists purely because a single QR code has a hard maximum data capacity, and
/// <see cref="ContactCardCodec"/>'s copy/paste-friendly format (JSON, with byte[] fields already
/// base64'd by System.Text.Json, then base64'd again as a whole) roughly doubles the payload size
/// on top of what the real bytes need. Measured against a real ML-KEM-768 key/ciphertext: a
/// contact card blob is ~2240 chars, an invite blob (which also carries a handshake ciphertext) is
/// ~4248 chars — comfortably past a QR code's absolute maximum (2953 bytes at the lowest error
/// correction, byte mode, largest version). This codec instead packs fields as raw
/// length-prefixed bytes with no JSON/base64 layer, then maps those bytes 1:1 onto a string via
/// <see cref="Encoding.Latin1"/> (a lossless byte&lt;-&gt;char bijection for every value 0-255 —
/// not a text encoding of anything meaningful, just a way to hand raw bytes to an API that wants a
/// string) and generates/reads the QR with an explicit ISO-8859-1 character-set hint on both ends
/// so ZXing doesn't re-encode it as UTF-8 and corrupt the high bytes. A real encode-QR-decode
/// round trip with a real 1184-byte public key + 1088-byte ciphertext was verified byte-for-byte
/// before this was written — the packed invite payload lands around 2.3KB, safely under the cap
/// this codec's ContactCardCodec-based sibling never had to worry about at copy/paste sizes.
///
/// Deliberately narrower than ContactCardCodec: only handles these two known shapes (not generic
/// like ContactCardCodec's <c>Encode&lt;T&gt;</c>), since a hand-rolled binary layout has to know
/// each type's fields. A scanned QR is converted back through <see cref="ContactCardCodec"/>'s own
/// format immediately (see NewChatViewModel.OnPeerCardScanned/OnInviteScanned) so
/// CreateSessionAsync/AcceptInviteAsync never need to know a QR was involved at all.
/// </summary>
public static class QrBlobCodec
{
    /// <summary>What ZXing must be told on both the generator and reader side — without this hint on both, high (128-255) byte values sent for a lower one get UTF-8-re-encoded on the way in, or misread on the way out.</summary>
    public const string CharacterSet = "ISO-8859-1";

    public static string EncodeContactCard(ContactCardBlob card)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            WriteContactCard(writer, card);
        return Encoding.Latin1.GetString(stream.ToArray());
    }

    public static ContactCardBlob DecodeContactCard(string qrValue)
    {
        using var stream = new MemoryStream(Encoding.Latin1.GetBytes(qrValue));
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        return ReadContactCard(reader);
    }

    public static string EncodeInvite(ChatInviteBlob invite)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            WriteContactCard(writer, new ContactCardBlob(invite.InitiatorDisplayName, invite.InitiatorPublicKey, invite.InitiatorRelayDeviceId));
            WriteLengthPrefixed(writer, invite.HandshakeCipherText);
        }
        return Encoding.Latin1.GetString(stream.ToArray());
    }

    public static ChatInviteBlob DecodeInvite(string qrValue)
    {
        using var stream = new MemoryStream(Encoding.Latin1.GetBytes(qrValue));
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        var card = ReadContactCard(reader);
        var cipherText = ReadLengthPrefixed(reader);
        return new ChatInviteBlob(card.DisplayName, card.PublicKey, card.RelayDeviceId, cipherText);
    }

    private static void WriteContactCard(BinaryWriter writer, ContactCardBlob card)
    {
        var nameBytes = Encoding.UTF8.GetBytes(card.DisplayName);
        if (nameBytes.Length > byte.MaxValue)
            throw new InvalidOperationException("Display name is too long to fit in a QR-packed contact card.");
        writer.Write((byte)nameBytes.Length);
        writer.Write(nameBytes);
        WriteLengthPrefixed(writer, card.PublicKey);
        writer.Write(card.RelayDeviceId.ToByteArray());
    }

    private static ContactCardBlob ReadContactCard(BinaryReader reader)
    {
        var nameLength = reader.ReadByte();
        var displayName = Encoding.UTF8.GetString(reader.ReadBytes(nameLength));
        var publicKey = ReadLengthPrefixed(reader);
        var relayDeviceId = new Guid(reader.ReadBytes(16));
        return new ContactCardBlob(displayName, publicKey, relayDeviceId);
    }

    private static void WriteLengthPrefixed(BinaryWriter writer, byte[] data)
    {
        writer.Write((ushort)data.Length);
        writer.Write(data);
    }

    private static byte[] ReadLengthPrefixed(BinaryReader reader)
    {
        var length = reader.ReadUInt16();
        return reader.ReadBytes(length);
    }
}
