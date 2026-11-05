namespace InkLink.Catalog.Domain;

public enum ArtworkKind { Tattoo, Flash }

/// <summary>
/// Obra publicada. A imagem é enviada pelo artista e guardada pelo Catálogo (decisão 0018); obras
/// anteriores com URL externa continuam exibidas até a troca da imagem.
/// </summary>
public sealed class Artwork
{
    private Artwork() { }
    public Guid Id { get; private set; }
    public Guid ArtistId { get; private set; }
    public ArtworkKind Kind { get; private set; }
    public string Title { get; private set; } = string.Empty;
    /// <summary>URL externa legada (obras publicadas antes do upload). Nula para imagens enviadas.</summary>
    public string? ImageUrl { get; private set; }
    /// <summary>Prefixo dos objetos da imagem enviada no armazenamento do Catálogo.</summary>
    public string? ImageKey { get; private set; }
    public string AlternativeText { get; private set; } = string.Empty;
    public string Style { get; private set; } = string.Empty;
    public decimal? ReferencePrice { get; private set; }

    public bool HasExternalImage => ImageKey is null;

    public static Artwork Create(Guid artistId, ArtworkKind kind, string? title, string? alternativeText, string? style, decimal? referencePrice, string imageKey)
    {
        if (artistId == Guid.Empty || !Enum.IsDefined(kind)) throw new InvalidCatalogData("artist", "Artist and artwork kind are required.");
        var artwork = new Artwork { Id = Guid.CreateVersion7(), ArtistId = artistId, Kind = kind };
        artwork.UpdateDetails(title, alternativeText, style, referencePrice);
        artwork.ImageKey = ValidKey(imageKey);
        return artwork;
    }

    /// <summary>Valida os dados descritivos antes de qualquer envio de arquivo.</summary>
    public static void ValidateDetails(ArtworkKind kind, string? title, string? alternativeText, string? style, decimal? referencePrice)
    {
        CatalogGuard.Text(title, 160, "title");
        CatalogGuard.Text(alternativeText, 300, "alternativeText");
        CatalogGuard.Text(style, 80, "style");
        if (referencePrice.HasValue) CatalogGuard.Price(referencePrice.Value, "referencePrice");
        if (kind == ArtworkKind.Tattoo && referencePrice is not null) throw new InvalidCatalogData("referencePrice", "Reference prices belong to flashes.");
    }

    public void UpdateDetails(string? title, string? alternativeText, string? style, decimal? referencePrice)
    {
        ValidateDetails(Kind, title, alternativeText, style, referencePrice);
        Title = title!.Trim(); AlternativeText = alternativeText!.Trim(); Style = style!.Trim(); ReferencePrice = referencePrice;
    }

    /// <summary>Troca a imagem e devolve o prefixo anterior, quando havia imagem enviada.</summary>
    public string? ReplaceImage(string imageKey)
    {
        var previous = ImageKey;
        ImageKey = ValidKey(imageKey);
        ImageUrl = null;
        return previous;
    }

    private static string ValidKey(string? key) =>
        string.IsNullOrWhiteSpace(key) || key.Length > 200 ? throw new InvalidCatalogData("image", "The uploaded image is required.") : key;
}
