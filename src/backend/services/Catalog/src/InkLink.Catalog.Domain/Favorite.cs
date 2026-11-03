namespace InkLink.Catalog.Domain;

public sealed class Favorite
{
    private Favorite() { }
    public Guid ClientId { get; private set; }
    public Guid ArtistId { get; private set; }
    public static Favorite Create(Guid clientId, Guid artistId)
    {
        if (clientId == Guid.Empty || artistId == Guid.Empty) throw new InvalidCatalogData("favorite", "Client and artist are required.");
        return new Favorite { ClientId = clientId, ArtistId = artistId };
    }
}
