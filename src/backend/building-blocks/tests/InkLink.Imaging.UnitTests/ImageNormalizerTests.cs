using SkiaSharp;

namespace InkLink.Imaging.UnitTests;

public sealed class ImageNormalizerTests
{
    /// <summary>Imagem sintética: metade esquerda vermelha e direita azul.</summary>
    internal static byte[] Image(int width, int height, SKEncodedImageFormat format = SKEncodedImageFormat.Png)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Blue);
            using var red = new SKPaint { Color = SKColors.Red };
            canvas.DrawRect(0, 0, width / 2f, height, red);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }

    private static SKBitmap Decode(byte[] content) => SKBitmap.Decode(content);

    [Theory]
    [InlineData(SKEncodedImageFormat.Png)]
    [InlineData(SKEncodedImageFormat.Jpeg)]
    [InlineData(SKEncodedImageFormat.Webp)]
    public void Accepted_formats_become_webp_variants_without_upscaling(SKEncodedImageFormat format)
    {
        var variants = ImageNormalizer.Normalize(Image(2000, 1000, format), ImageRules.Artwork);
        Assert.Equal([1600, 480], variants.Select(variant => variant.Size));
        Assert.Equal((1600, 800), (variants[0].Width, variants[0].Height));
        Assert.Equal((480, 240), (variants[1].Width, variants[1].Height));
        Assert.All(variants, variant => Assert.Equal(SKEncodedImageFormat.Webp, ImageNormalizer.Detect(variant.Content)));

        var small = ImageNormalizer.Normalize(Image(800, 600, format), ImageRules.Artwork);
        Assert.Equal((800, 600), (small[0].Width, small[0].Height));
        Assert.Equal((480, 360), (small[1].Width, small[1].Height));
    }

    [Fact]
    public void Profile_photo_is_cropped_to_a_centered_square()
    {
        var variants = ImageNormalizer.Normalize(Image(1000, 600), ImageRules.ProfilePhoto);
        Assert.Equal((512, 512), (variants[0].Width, variants[0].Height));
        Assert.Equal((128, 128), (variants[1].Width, variants[1].Height));
    }

    [Fact]
    public void Exif_orientation_is_applied_and_metadata_is_dropped()
    {
        var jpeg = WithExifOrientation(Image(800, 600, SKEncodedImageFormat.Jpeg), 6);
        var variant = ImageNormalizer.Normalize(jpeg, ImageRules.Artwork)[0];
        Assert.Equal((600, 800), (variant.Width, variant.Height));
        using var bitmap = Decode(variant.Content);
        // Rotação de 90° no sentido horário: a metade esquerda (vermelha) passa para o topo.
        Assert.True(bitmap.GetPixel(300, 100).Red > 200);
        Assert.True(bitmap.GetPixel(300, 700).Blue > 200);
        Assert.DoesNotContain("Exif"u8.ToArray(), Windows(variant.Content, 4));
    }

    [Theory]
    [InlineData("image.empty")]
    [InlineData("image.format")]
    [InlineData("image.format.renamed")]
    [InlineData("image.format.corrupted")]
    [InlineData("image.dimensions")]
    [InlineData("image.too_large")]
    public void Invalid_files_are_rejected_before_any_variant(string scenario)
    {
        var content = scenario switch
        {
            "image.empty" => [],
            "image.format" => "GIF89a\u0001\u0000\u0001\u0000"u8.ToArray(),
            "image.format.renamed" => "<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray(),
            "image.format.corrupted" => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4, 5, 6],
            "image.dimensions" => Image(900, 500),
            _ => new byte[ImageRules.Artwork.MaxBytes + 1],
        };
        var error = Assert.Throws<InvalidImageException>(() => ImageNormalizer.Normalize(content, ImageRules.Artwork));
        Assert.StartsWith(scenario.Split('.')[0] + "." + scenario.Split('.')[1], error.Code);
    }

    private static IEnumerable<byte[]> Windows(byte[] content, int size)
    {
        for (var i = 0; i + size <= content.Length; i++) yield return content[i..(i + size)];
    }

    /// <summary>Insere um segmento APP1 (EXIF) com a tag Orientation logo após o SOI do JPEG.</summary>
    private static byte[] WithExifOrientation(byte[] jpeg, ushort orientation)
    {
        byte[] tiff = [0x4D, 0x4D, 0x00, 0x2A, 0x00, 0x00, 0x00, 0x08, 0x00, 0x01, 0x01, 0x12, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01,
            (byte)(orientation >> 8), (byte)orientation, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
        var payload = "Exif\0\0"u8.ToArray().Concat(tiff).ToArray();
        var length = payload.Length + 2;
        byte[] segment = [0xFF, 0xE1, (byte)(length >> 8), (byte)length, .. payload];
        return [.. jpeg[..2], .. segment, .. jpeg[2..]];
    }
}
