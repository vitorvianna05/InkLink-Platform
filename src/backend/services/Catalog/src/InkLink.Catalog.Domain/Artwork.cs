namespace InkLink.Catalog.Domain;

public enum ArtworkKind { Tattoo, Flash }

public sealed class Artwork
{
    private Artwork() { }
    public Guid Id { get; private set; }
    public Guid ArtistId { get; private set; }
    public ArtworkKind Kind { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string ImageUrl { get; private set; } = string.Empty;
    public string AlternativeText { get; private set; } = string.Empty;
    public string Style { get; private set; } = string.Empty;
    public decimal? ReferencePrice { get; private set; }

    public static Artwork Create(Guid artistId, ArtworkKind kind, string? title, string? imageUrl, string? alternativeText, string? style, decimal? referencePrice)
    {
        if (artistId == Guid.Empty || !Enum.IsDefined(kind)) throw new InvalidCatalogData("artist", "Artist and artwork kind are required.");
        var artwork = new Artwork { Id = Guid.CreateVersion7(), ArtistId = artistId, Kind = kind };
        artwork.Update(title, imageUrl, alternativeText, style, referencePrice);
        return artwork;
    }

    public void Update(string? title, string? imageUrl, string? alternativeText, string? style, decimal? referencePrice)
    {
        var validTitle = CatalogGuard.Text(title, 160, "title");
        var validUrl = CatalogGuard.Text(imageUrl, 2048, "imageUrl");
        if (!Uri.TryCreate(validUrl, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(url.UserInfo) || url.IsLoopback || !string.IsNullOrEmpty(url.Query) || !string.IsNullOrEmpty(url.Fragment))
            throw new InvalidCatalogData("imageUrl", "Use a public HTTPS image URL without credentials, query or fragment.");
        var validAlternativeText = CatalogGuard.Text(alternativeText, 300, "alternativeText");
        var validStyle = CatalogGuard.Text(style, 80, "style");
        if (referencePrice.HasValue) CatalogGuard.Price(referencePrice.Value, "referencePrice");
        if (Kind == ArtworkKind.Tattoo && referencePrice is not null) throw new InvalidCatalogData("referencePrice", "Reference prices belong to flashes.");
        Title = validTitle; ImageUrl = validUrl; AlternativeText = validAlternativeText; Style = validStyle; ReferencePrice = referencePrice;
    }
}
