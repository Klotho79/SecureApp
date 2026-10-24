namespace SecureApp.Presentation.Chat;

/// <summary>
/// The QR character-set hint shared by <see cref="QrImageGenerator"/>. It used to be a whole binary codec
/// that packed a contact card / chat invite into a QR code (and a reader for the camera scanner); that
/// pairing path was dropped on 2026-09-16 (members are paired from the relay directory) and the codec
/// itself was deleted on 2026-10-24. QR codes are only generated now — the download link and the
/// WireGuard config in Settings.
/// </summary>
public static class QrBlobCodec
{
    /// <summary>What ZXing must be told when generating the QR — without it, high (128-255) byte values get UTF-8-re-encoded and corrupted.</summary>
    public const string CharacterSet = "ISO-8859-1";
}
