using SkiaSharp;

namespace Lore.Api.Media;

public static class Skia
{
    /// <summary>Display size after EXIF orientation, or null if Skia can't decode the format (e.g. HEIC on Linux).</summary>
    public static (int Width, int Height)? Dimensions(string path)
    {
        using var codec = SKCodec.Create(path);
        if (codec == null) return null;
        var (w, h) = (codec.Info.Width, codec.Info.Height);
        return Swaps(codec.EncodedOrigin) ? (h, w) : (w, h);
    }

    /// <summary>Decodes, fits within maxSize, applies EXIF orientation, writes JPEG. False if undecodable.</summary>
    public static bool WriteJpegThumbnail(Stream input, string output, int maxSize)
    {
        using var codec = SKCodec.Create(input);
        if (codec == null) return false;

        // Let the codec downscale during decode (cheap for JPEG), then finish with a quality resize.
        var scale = Math.Min(1f, (float)maxSize / Math.Max(codec.Info.Width, codec.Info.Height));
        var decodeSize = codec.GetScaledDimensions(scale);
        using var decoded = SKBitmap.Decode(codec, codec.Info.WithSize(decodeSize.Width, decodeSize.Height));
        if (decoded == null) return false;

        var fit = Math.Min(1f, (float)maxSize / Math.Max(decoded.Width, decoded.Height));
        using var resized = fit < 1f
            ? decoded.Resize(new SKImageInfo((int)Math.Round(decoded.Width * fit), (int)Math.Round(decoded.Height * fit)), new SKSamplingOptions(SKCubicResampler.Mitchell))
            : null;
        using var oriented = Orient(resized ?? decoded, codec.EncodedOrigin);

        using var image = SKImage.FromBitmap(oriented ?? resized ?? decoded);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 80);
        using var file = File.Create(output);
        data.SaveTo(file);
        return true;
    }

    static bool Swaps(SKEncodedOrigin o) => o is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;

    /// <returns>A new upright bitmap, or null when no transform is needed.</returns>
    static SKBitmap? Orient(SKBitmap src, SKEncodedOrigin origin)
    {
        float w = src.Width, h = src.Height;
        // x' = a*x + b*y + c, y' = d*x + e*y + f for each EXIF orientation.
        SKMatrix? m = origin switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
            SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
            _ => null,
        };
        if (m == null) return null;

        var dst = Swaps(origin) ? new SKBitmap(src.Height, src.Width) : new SKBitmap(src.Width, src.Height);
        using var canvas = new SKCanvas(dst);
        canvas.SetMatrix(m.Value);
        using var image = SKImage.FromBitmap(src);
        canvas.DrawImage(image, 0, 0, SKSamplingOptions.Default);
        return dst;
    }
}
