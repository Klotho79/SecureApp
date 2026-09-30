using System.Text;
using SkiaSharp;

namespace SecureApp.Presentation.Rendering;

/// <summary>
/// Invisible, pixel-level watermark (2026-09-30, user's own ask, layered on top of the visible
/// moving username+timestamp+IP overlay <c>DocumentViewerViewModel</c> already draws — that one
/// stays; this is an ADDITIONAL layer, not a replacement).
///
/// Embeds who viewed a rendered page + when in the least-significant bit of each R/G/B channel
/// of the first <see cref="MaxPixels"/> pixels, the same short block repeated back-to-back for
/// redundancy — recoverable by <c>SecureApp.WatermarkDecoder</c> (see that tool's own README).
///
/// Honest limits (told to the user up front, not discovered later): this survives a LOSSLESS
/// digital copy of the rendered PNG perfectly (an unmodified file export, a re-save as PNG). It
/// does NOT reliably survive a JPEG re-save, a resize, or — the actual remaining leak vector
/// FLAG_SECURE can't block — someone re-photographing the physical screen with a camera: sensor
/// noise, auto-exposure, JPEG compression and moiré all scramble least-significant bits near-
/// randomly. This is defense-in-depth for a DIGITAL leak, not a guaranteed catch for a
/// photographed one — the visible overlay is what still has to do that job for photos.
/// </summary>
public static class PixelWatermark
{
    /// <summary>"SAW1" — SecureApp Watermark, format v1. Lets the decoder immediately reject a plain unwatermarked image instead of returning garbage.</summary>
    private static readonly byte[] Magic = [0x53, 0x41, 0x57, 0x31];

    /// <summary>Bounds the cost on a large page render — no need to touch every pixel, only enough to repeat the (short) payload many times over for redundancy against partial cropping.</summary>
    private const int MaxPixels = 60_000;

    public static void Embed(SKBitmap bitmap, string payload)
    {
        var block = BuildBlock(payload);
        var bits = ToBits(block);

        var pixelCount = Math.Min(bitmap.Width * bitmap.Height, MaxPixels);
        var pixels = bitmap.Pixels; // one bulk marshal in, not millions of GetPixel calls
        var bitIndex = 0;
        for (var i = 0; i < pixelCount; i++)
        {
            var c = pixels[i];
            var r = SetLsb(c.Red, bits[bitIndex % bits.Length]); bitIndex++;
            var g = SetLsb(c.Green, bits[bitIndex % bits.Length]); bitIndex++;
            var b = SetLsb(c.Blue, bits[bitIndex % bits.Length]); bitIndex++;
            pixels[i] = new SKColor(r, g, b, c.Alpha);
        }
        bitmap.Pixels = pixels; // one bulk marshal back out
    }

    /// <summary>[Magic(4) | Length(2, big-endian) | UTF8 payload (truncated to fit ushort) | CRC16(2) of the payload bytes].</summary>
    private static byte[] BuildBlock(string payload)
    {
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        if (payloadBytes.Length > ushort.MaxValue) payloadBytes = payloadBytes[..ushort.MaxValue];

        var block = new byte[Magic.Length + 2 + payloadBytes.Length + 2];
        Magic.CopyTo(block, 0);
        var length = (ushort)payloadBytes.Length;
        block[4] = (byte)(length >> 8);
        block[5] = (byte)(length & 0xFF);
        payloadBytes.CopyTo(block, 6);
        var crc = Crc16.Compute(payloadBytes);
        block[^2] = (byte)(crc >> 8);
        block[^1] = (byte)(crc & 0xFF);
        return block;
    }

    private static bool[] ToBits(byte[] bytes)
    {
        var bits = new bool[bytes.Length * 8];
        for (var i = 0; i < bytes.Length; i++)
            for (var b = 0; b < 8; b++)
                bits[i * 8 + b] = ((bytes[i] >> (7 - b)) & 1) == 1;
        return bits;
    }

    private static byte SetLsb(byte channel, bool bit) => (byte)(bit ? channel | 1 : channel & 0xFE);
}

/// <summary>
/// Plain CRC-16/CCITT-FALSE — just enough to let <c>SecureApp.WatermarkDecoder</c> tell a genuine
/// (possibly degraded-but-still-readable) watermark block apart from random noise; not a
/// cryptographic integrity check (there's nothing secret or adversarial being protected against
/// here, only "is this really our own embedded block").
/// </summary>
internal static class Crc16
{
    public static ushort Compute(byte[] data)
    {
        ushort crc = 0xFFFF;
        foreach (var b in data)
        {
            crc ^= (ushort)(b << 8);
            for (var i = 0; i < 8; i++)
                crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
        }
        return crc;
    }
}
