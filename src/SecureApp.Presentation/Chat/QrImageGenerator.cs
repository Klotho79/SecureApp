using SkiaSharp;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;

namespace SecureApp.Presentation.Chat;

/// <summary>
/// Renders a QR code to PNG bytes directly via ZXing.Net's core encoder + the SkiaSharp binding —
/// bypassing <c>ZXing.Net.Maui.Controls.BarcodeGeneratorView</c> entirely. Real, confirmed bug
/// found live (2026-09-05): that control renders nothing at all on Windows (no exception, no log,
/// just a blank space) while the exact same value renders correctly on Android — a platform gap in
/// the library's Windows handler, not anything wrong with the encoded data (verified separately
/// with a real ZXing.Net encode→decode round trip before this was written, see QrBlobCodec's own
/// remarks). This sidesteps that gap on every platform, reusing the same "PNG bytes -> Image via
/// ImageSource.FromStream" pattern <c>DocumentRenderingService</c>/<c>DocumentViewerViewModel</c>
/// already use, rather than depending on a platform-specific view handler at all.
/// </summary>
public static class QrImageGenerator
{
    /// <summary>
    /// <paramref name="errorCorrection"/> defaults to L (the lowest level) because the invite
    /// blob's payload (it carries a full handshake ciphertext) sits right at a QR code's absolute
    /// maximum capacity — see QrBlobCodec's own remarks for the exact numbers — so there is no
    /// spare capacity to raise it there. The smaller contact-card payload has comfortable headroom
    /// and should pass Q explicitly for better resilience to camera blur/partial occlusion.
    /// </summary>
    public static byte[] GeneratePng(string value, int sizePx = 700, ZXing.QrCode.Internal.ErrorCorrectionLevel? errorCorrection = null)
    {
        var writer = new ZXing.SkiaSharp.BarcodeWriter
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new QrCodeEncodingOptions
            {
                ErrorCorrection = errorCorrection ?? ZXing.QrCode.Internal.ErrorCorrectionLevel.L,
                CharacterSet = QrBlobCodec.CharacterSet,
                Margin = 4,
                Width = sizePx,
                Height = sizePx
            }
        };

        using var bitmap = writer.Write(value);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
