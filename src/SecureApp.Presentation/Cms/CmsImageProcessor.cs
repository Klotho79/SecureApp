using SkiaSharp;

namespace SecureApp.Presentation.Cms;

/// <summary>Decodes a photo (honouring EXIF orientation) and produces width variants for responsive display.</summary>
public static class CmsImageProcessor
{
    public static readonly int[] VariantWidths = [480, 960, 1800];

    public sealed record Prepared(double AspectRatio, byte[] PreviewJpeg);

    public static Prepared Prepare(byte[] source)
    {
        using var bitmap = DecodeOriented(source);
        var preview = Resize(bitmap, Math.Min(960, bitmap.Width));
        return new Prepared((double)bitmap.Width / bitmap.Height, preview);
    }

    /// <summary>Variants no wider than the source (at least one, even for a tiny image).</summary>
    public static IReadOnlyList<(int Width, byte[] Jpeg)> MakeVariants(byte[] source)
    {
        using var bitmap = DecodeOriented(source);
        var widths = VariantWidths.Where(w => w < bitmap.Width).ToList();
        widths.Add(Math.Min(bitmap.Width, VariantWidths[^1]));
        return widths.Distinct().Select(w => (w, Resize(bitmap, w))).ToList();
    }

    private static byte[] Resize(SKBitmap bitmap, int width)
    {
        var height = Math.Max(1, (int)Math.Round((double)bitmap.Height * width / bitmap.Width));
        using var resized = bitmap.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
            ?? throw new InvalidOperationException("Obrázek se nepodařilo zmenšit.");
        using var image = SKImage.FromBitmap(resized);
        return image.Encode(SKEncodedImageFormat.Jpeg, 85).ToArray();
    }

    private static SKBitmap DecodeOriented(byte[] source)
    {
        using var data = SKData.CreateCopy(source);
        using var codec = SKCodec.Create(data) ?? throw new InvalidOperationException("Soubor není podporovaný obrázek.");
        var origin = codec.EncodedOrigin;
        var bitmap = SKBitmap.Decode(codec) ?? throw new InvalidOperationException("Obrázek se nepodařilo načíst.");

        var (swap, angle, flipX) = origin switch
        {
            SKEncodedOrigin.BottomRight => (false, 180f, false),
            SKEncodedOrigin.RightTop => (true, 90f, false),
            SKEncodedOrigin.LeftBottom => (true, 270f, false),
            SKEncodedOrigin.TopRight => (false, 0f, true),
            _ => (false, 0f, false)
        };
        if (angle == 0 && !flipX) return bitmap;

        var rotated = new SKBitmap(swap ? bitmap.Height : bitmap.Width, swap ? bitmap.Width : bitmap.Height);
        using (var canvas = new SKCanvas(rotated))
        {
            canvas.Translate(rotated.Width / 2f, rotated.Height / 2f);
            if (angle != 0) canvas.RotateDegrees(angle);
            if (flipX) canvas.Scale(-1, 1);
            canvas.Translate(-bitmap.Width / 2f, -bitmap.Height / 2f);
            canvas.DrawBitmap(bitmap, 0, 0);
        }
        bitmap.Dispose();
        return rotated;
    }
}
