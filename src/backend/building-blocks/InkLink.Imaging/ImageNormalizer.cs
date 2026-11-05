using SkiaSharp;

namespace InkLink.Imaging;

/// <summary>Regras de aceitação e variantes geradas (decisão 0018).</summary>
/// <param name="MaxBytes">Tamanho máximo do arquivo enviado.</param>
/// <param name="MinShortSide">Menor lado mínimo, em pixels, depois da orientação.</param>
/// <param name="Variants">Lado maior de cada variante, em pixels; nunca amplia a imagem.</param>
/// <param name="Square">Recorta ao centro em formato quadrado (fotos de perfil).</param>
public sealed record ImageRules(long MaxBytes, int MinShortSide, IReadOnlyList<int> Variants, bool Square = false)
{
    public static readonly ImageRules Artwork = new(10 * 1024 * 1024, 600, [1600, 480]);
    public static readonly ImageRules ProfilePhoto = new(5 * 1024 * 1024, 256, [512, 128], Square: true);
}

public sealed record ImageVariant(int Size, int Width, int Height, byte[] Content)
{
    public const string ContentType = "image/webp";
    public string FileName => $"{Size}.webp";
}

/// <summary>Arquivo recusado: formato, tamanho ou dimensões fora das regras.</summary>
public sealed class InvalidImageException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// Decodifica JPEG, PNG ou WebP verificados pelo conteúdo, aplica a orientação, descarta metadados
/// (inclusive localização) e regrava as variantes em WebP.
/// </summary>
public static class ImageNormalizer
{
    private const long MaxPixels = 40_000_000;
    private const int Quality = 82;

    public static IReadOnlyList<ImageVariant> Normalize(byte[] content, ImageRules rules)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length == 0) throw new InvalidImageException("image.empty", "The image file is empty.");
        if (content.Length > rules.MaxBytes) throw new InvalidImageException("image.too_large", $"The image must have at most {rules.MaxBytes / (1024 * 1024)} MB.");
        var expected = Detect(content) ?? throw new InvalidImageException("image.format", "Use a JPEG, PNG or WebP image.");

        using var data = SKData.CreateCopy(content);
        using var codec = SKCodec.Create(data) ?? throw new InvalidImageException("image.format", "The image could not be read.");
        if (codec.EncodedFormat != expected) throw new InvalidImageException("image.format", "The image content does not match its format.");
        if (codec.FrameCount > 1) throw new InvalidImageException("image.animated", "Animated images are not supported.");
        if ((long)codec.Info.Width * codec.Info.Height > MaxPixels) throw new InvalidImageException("image.dimensions", "The image has too many pixels.");

        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var decoded = new SKBitmap(info);
        var result = codec.GetPixels(info, decoded.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
            throw new InvalidImageException("image.format", "The image could not be decoded.");

        using var oriented = Orient(decoded, codec.EncodedOrigin);
        using var source = rules.Square ? CropSquare(oriented) : oriented.Copy();
        if (Math.Min(source.Width, source.Height) < rules.MinShortSide)
            throw new InvalidImageException("image.dimensions", $"The image must have at least {rules.MinShortSide} px on its shorter side.");

        var variants = new List<ImageVariant>(rules.Variants.Count);
        foreach (var size in rules.Variants)
        {
            var scale = Math.Min(1d, (double)size / Math.Max(source.Width, source.Height));
            var width = Math.Max(1, (int)Math.Round(source.Width * scale));
            var height = Math.Max(1, (int)Math.Round(source.Height * scale));
            using var resized = source.Resize(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul), new SKSamplingOptions(SKCubicResampler.Mitchell))
                ?? throw new InvalidImageException("image.format", "The image could not be resized.");
            using var image = SKImage.FromBitmap(resized);
            using var encoded = image.Encode(SKEncodedImageFormat.Webp, Quality)
                ?? throw new InvalidOperationException("WebP encoding is unavailable.");
            variants.Add(new ImageVariant(size, width, height, encoded.ToArray()));
        }
        return variants;
    }

    /// <summary>Formato pelos primeiros bytes; a extensão e o Content-Type enviados não são confiáveis.</summary>
    public static SKEncodedImageFormat? Detect(ReadOnlySpan<byte> content)
    {
        if (content.Length >= 3 && content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF) return SKEncodedImageFormat.Jpeg;
        if (content.Length >= 8 && content[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return SKEncodedImageFormat.Png;
        if (content.Length >= 12 && content[..4].SequenceEqual("RIFF"u8) && content[8..12].SequenceEqual("WEBP"u8)) return SKEncodedImageFormat.Webp;
        return null;
    }

    /// <summary>
    /// Aplica a orientação EXIF. Transformações do canvas valem na ordem inversa das chamadas; para
    /// a largura final W, o pixel (x, y) vai para: 90° (W - y, x); 270° (y, H' - x); transposição (y, x).
    /// </summary>
    private static SKBitmap Orient(SKBitmap bitmap, SKEncodedOrigin origin)
    {
        if (origin is SKEncodedOrigin.TopLeft or SKEncodedOrigin.Default) return bitmap.Copy();
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var result = new SKBitmap(swap ? bitmap.Height : bitmap.Width, swap ? bitmap.Width : bitmap.Height, bitmap.ColorType, bitmap.AlphaType);
        using (var canvas = new SKCanvas(result))
        {
            float w = result.Width, h = result.Height;
            switch (origin)
            {
                case SKEncodedOrigin.TopRight: canvas.Scale(-1, 1, w / 2, 0); break;
                case SKEncodedOrigin.BottomRight: canvas.RotateDegrees(180, w / 2, h / 2); break;
                case SKEncodedOrigin.BottomLeft: canvas.Scale(1, -1, 0, h / 2); break;
                case SKEncodedOrigin.LeftTop: canvas.Scale(-1, 1, w / 2, 0); canvas.Translate(w, 0); canvas.RotateDegrees(90); break;
                case SKEncodedOrigin.RightTop: canvas.Translate(w, 0); canvas.RotateDegrees(90); break;
                case SKEncodedOrigin.RightBottom: canvas.Scale(-1, 1, w / 2, 0); canvas.Translate(0, h); canvas.RotateDegrees(270); break;
                case SKEncodedOrigin.LeftBottom: canvas.Translate(0, h); canvas.RotateDegrees(270); break;
            }
            using var image = SKImage.FromBitmap(bitmap);
            canvas.DrawImage(image, 0, 0, SKSamplingOptions.Default);
        }
        return result;
    }

    private static SKBitmap CropSquare(SKBitmap bitmap)
    {
        var side = Math.Min(bitmap.Width, bitmap.Height);
        using var subset = new SKBitmap();
        if (!bitmap.ExtractSubset(subset, SKRectI.Create((bitmap.Width - side) / 2, (bitmap.Height - side) / 2, side, side)))
            throw new InvalidImageException("image.format", "The image could not be cropped.");
        return subset.Copy();
    }
}
