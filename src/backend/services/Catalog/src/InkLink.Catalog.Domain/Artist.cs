namespace InkLink.Catalog.Domain;

public sealed class Artist
{
    private Artist() { }
    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Studio { get; private set; } = string.Empty;
    public string City { get; private set; } = string.Empty;
    public string Biography { get; private set; } = string.Empty;
    public string[] Styles { get; private set; } = [];
    public decimal StartingPrice { get; private set; }

    public static Artist Create(Guid ownerId, string? name, string? studio, string? city, string? biography, string[]? styles, decimal startingPrice)
    {
        if (ownerId == Guid.Empty) throw new InvalidCatalogData("owner", "An owner is required.");
        var artist = new Artist { Id = Guid.CreateVersion7(), OwnerId = ownerId };
        artist.Update(name, studio, city, biography, styles, startingPrice);
        return artist;
    }

    public void Update(string? name, string? studio, string? city, string? biography, string[]? styles, decimal startingPrice)
    {
        var validName = CatalogGuard.Text(name, 120, "name");
        var validStudio = CatalogGuard.Text(studio, 160, "studio");
        var validCity = CatalogGuard.Text(city, 100, "city");
        var validBiography = CatalogGuard.Text(biography, 4000, "biography");
        if (styles is null || styles.Length is < 1 or > 20) throw new InvalidCatalogData("styles", "Provide between 1 and 20 styles.");
        var validStyles = styles.Select(style => CatalogGuard.Text(style, 80, "styles")).ToArray();
        if (validStyles.Any(style => SearchText.Normalize(style).Length == 0) || validStyles.Select(SearchText.Normalize).Distinct().Count() != validStyles.Length)
            throw new InvalidCatalogData("styles", "Styles must be searchable and unique regardless of case and accents.");
        if (new[] { validName, validStudio, validCity }.Any(value => SearchText.Normalize(value).Length == 0))
            throw new InvalidCatalogData("name", "Name, studio and city must contain letters or digits.");
        var validPrice = CatalogGuard.Price(startingPrice, "startingPrice");
        Name = validName; Studio = validStudio; City = validCity; Biography = validBiography; Styles = validStyles; StartingPrice = validPrice;
    }
}
