using SkiaSharp;

// SecureApp.WatermarkDecoder — reads the invisible pixel-level watermark PixelWatermark.cs (in the
// main app, src/SecureApp.Presentation/Rendering/) embeds into every rendered document/photo page.
// Deliberately a SEPARATE, standalone tool rather than a feature inside SecureApp itself: it's for
// the institution's own IT/security team to run against a suspected leaked image, on a PC, outside
// the app entirely — see this repo's SECURITY.md / keystore/PRISTUPY.md for who that is.
//
// Extraction logic here is a DELIBERATE DUPLICATE of PixelWatermark.Embed's own bit layout (same
// "duplicated, stays independently readable" call this codebase already makes elsewhere, e.g.
// HttpSharedContactService's device-auth header helper) — this tool has no reference to the main
// app's assemblies at all, on purpose, so it stays a single self-contained .exe an investigator can
// run with nothing else installed but a .NET runtime.
//
// Honest limits (same ones PixelWatermark.cs itself states): this only recovers a watermark from a
// LOSSLESS digital copy (an exported/re-saved PNG). A JPEG re-save, a resize, or — the main real-
// world case — a photograph of the physical screen almost always destroys the least-significant
// bits this relies on. "No watermark found" does NOT mean "nobody leaked this" — it just means this
// particular recovery technique didn't work on this particular copy.

if (args.Length != 1 || args[0] is "-h" or "--help")
{
    Console.WriteLine("Použití: secureapp-watermark-decoder <cesta-k-obrázku>");
    Console.WriteLine();
    Console.WriteLine("Zkusí z obrázku přečíst neviditelný vodoznak, který SecureApp vkládá do");
    Console.WriteLine("každé vykreslené stránky dokumentu/fotky (jméno prohlížejícího + čas UTC).");
    Console.WriteLine("Funguje spolehlivě jen na nezměněnou digitální kopii (PNG export) — na");
    Console.WriteLine("vyfocený displej prakticky nikdy, tam vodoznak zničí šum fotoaparátu a JPEG komprese.");
    return args.Length == 1 ? 0 : 1;
}

var path = args[0];
if (!File.Exists(path))
{
    Console.Error.WriteLine($"Soubor nenalezen: {path}");
    return 2;
}

using var bitmap = SKBitmap.Decode(path);
if (bitmap is null)
{
    Console.Error.WriteLine("Soubor se nepodařilo přečíst jako obrázek.");
    return 2;
}

var result = WatermarkExtractor.TryExtract(bitmap);
if (result is null)
{
    Console.WriteLine("Vodoznak nenalezen (viz omezení výše — nemusí to znamenat, že tam nikdy nebyl).");
    return 3;
}

Console.WriteLine("Vodoznak nalezen:");
Console.WriteLine(result);
return 0;

internal static class WatermarkExtractor
{
    private static readonly byte[] Magic = [0x53, 0x41, 0x57, 0x31]; // "SAW1" — must match PixelWatermark.Magic exactly
    private const int MaxPixels = 60_000; // must match PixelWatermark.MaxPixels

    public static string? TryExtract(SKBitmap bitmap)
    {
        var pixelCount = Math.Min(bitmap.Width * bitmap.Height, MaxPixels);
        var pixels = bitmap.Pixels;
        var totalBits = pixelCount * 3;
        if (totalBits < (Magic.Length + 2) * 8) return null; // too small to even hold the header

        bool Bit(int index)
        {
            var pixel = pixels[index / 3];
            // NOTE: `index % 3 switch { ... }` (the switch expression chained directly onto `%`
            // with no parens) mis-evaluates at runtime — confirmed via a real round-trip test
            // (2026-09-30) where the R/G channels came back right but B never did. Binding the
            // modulo to its own variable first is the fix; do not "simplify" this back.
            var channelIndex = index % 3;
            var channel = channelIndex switch { 0 => pixel.Red, 1 => pixel.Green, _ => pixel.Blue };
            return (channel & 1) == 1;
        }

        byte ByteAt(int bitOffset)
        {
            byte value = 0;
            for (var b = 0; b < 8; b++)
                value = (byte)((value << 1) | (Bit(bitOffset + b) ? 1 : 0));
            return value;
        }

        // Header: 4 magic bytes + 2 length bytes, always at the very start — see PixelWatermark.Embed.
        for (var i = 0; i < Magic.Length; i++)
            if (ByteAt(i * 8) != Magic[i]) return null;

        var length = (ByteAt(Magic.Length * 8) << 8) | ByteAt((Magic.Length + 1) * 8);
        var headerBits = (Magic.Length + 2) * 8;
        var payloadBits = length * 8;
        var crcBits = 16;
        if (headerBits + payloadBits + crcBits > totalBits) return null; // claimed length doesn't fit — not a real match

        var payload = new byte[length];
        for (var i = 0; i < length; i++)
            payload[i] = ByteAt(headerBits + i * 8);

        var expectedCrc = (ByteAt(headerBits + payloadBits) << 8) | ByteAt(headerBits + payloadBits + 8);
        if (Crc16(payload) != expectedCrc) return null; // header matched by chance, or data corrupted — reject rather than print garbage

        return System.Text.Encoding.UTF8.GetString(payload);
    }

    /// <summary>CRC-16/CCITT-FALSE — must match PixelWatermark's Crc16.Compute exactly.</summary>
    private static int Crc16(byte[] data)
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
