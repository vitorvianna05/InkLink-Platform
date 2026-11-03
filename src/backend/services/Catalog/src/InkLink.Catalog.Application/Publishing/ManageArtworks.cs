using InkLink.Catalog.Application.Accounts;
using InkLink.Catalog.Application.Queries;
using InkLink.Catalog.Domain;

namespace InkLink.Catalog.Application.Publishing;

public sealed class ManageArtworks(ICurrentCatalogAccount current, IArtistStore store)
{
    public async Task<PublishedArtwork> CreateAsync(ArtworkKind kind, ArtworkInput input, CancellationToken cancellationToken)
    {
        var artist = await OwnArtistAsync(cancellationToken);
        var artwork = Artwork.Create(artist.Id, kind, input.Title, input.ImageUrl, input.AlternativeText, input.Style, input.ReferencePrice);
        await store.SaveArtworkAsync(artwork, true, cancellationToken);
        return new(artist.Id, View(artwork));
    }

    public async Task UpdateAsync(Guid artworkId, ArtworkKind kind, ArtworkInput input, CancellationToken cancellationToken)
    {
        var artist = await OwnArtistAsync(cancellationToken);
        var artwork = await store.FindArtworkAsync(artist.Id, artworkId, kind, cancellationToken)
            ?? throw new CatalogFailure(CatalogError.NotFound, "catalog.artwork_not_found");
        artwork.Update(input.Title, input.ImageUrl, input.AlternativeText, input.Style, input.ReferencePrice);
        await store.SaveArtworkAsync(artwork, false, cancellationToken);
    }

    public async Task DeleteAsync(Guid artworkId, ArtworkKind kind, CancellationToken cancellationToken)
    {
        var artist = await OwnArtistAsync(cancellationToken);
        var artwork = await store.FindArtworkAsync(artist.Id, artworkId, kind, cancellationToken)
            ?? throw new CatalogFailure(CatalogError.NotFound, "catalog.artwork_not_found");
        await store.DeleteArtworkAsync(artwork, cancellationToken);
    }

    private async Task<Artist> OwnArtistAsync(CancellationToken cancellationToken)
    {
        var account = AccountAccess.Require(current, artist: true);
        return await store.FindByOwnerAsync(account.Id, cancellationToken)
            ?? throw new CatalogFailure(CatalogError.NotFound, "catalog.artist_not_found");
    }

    private static ArtworkView View(Artwork artwork) => new(artwork.Id, artwork.Title, artwork.ImageUrl, artwork.AlternativeText, artwork.Style, artwork.ReferencePrice);
}


public sealed record PublishedArtwork(Guid ArtistId, ArtworkView Artwork);
