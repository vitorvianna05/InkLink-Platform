using InkLink.Catalog.Application.Accounts;
using InkLink.Catalog.Application.Queries;
using InkLink.Catalog.Domain;

namespace InkLink.Catalog.Application.Publishing;

public sealed record ArtistInput(string? Name, string? Studio, string? City, string? Biography, string[]? Styles, decimal? StartingPrice);
public sealed record ArtworkInput(string? Title, string? ImageUrl, string? AlternativeText, string? Style, decimal? ReferencePrice);
public sealed record PublishedArtist(ArtistProfile Profile, bool Created);
public interface IArtistStore
{
    Task<Artist?> FindByOwnerAsync(Guid ownerId, CancellationToken cancellationToken);
    Task SaveArtistAsync(Artist artist, bool created, CancellationToken cancellationToken);
    Task<Artwork?> FindArtworkAsync(Guid artistId, Guid artworkId, ArtworkKind kind, CancellationToken cancellationToken);
    Task SaveArtworkAsync(Artwork artwork, bool created, CancellationToken cancellationToken);
    Task DeleteArtworkAsync(Artwork artwork, CancellationToken cancellationToken);
}

public sealed class PublishArtist(ICurrentCatalogAccount current, IArtistStore store)
{
    public async Task<PublishedArtist> ExecuteAsync(ArtistInput input, CancellationToken cancellationToken)
    {
        var account = AccountAccess.Require(current, artist: true);
        var startingPrice = input.StartingPrice ?? throw CatalogFailure.Invalid("startingPrice", "Starting price is required.");
        var artist = await store.FindByOwnerAsync(account.Id, cancellationToken);
        var created = artist is null;
        if (created) artist = Artist.Create(account.Id, input.Name, input.Studio, input.City, input.Biography, input.Styles, startingPrice);
        else artist!.Update(input.Name, input.Studio, input.City, input.Biography, input.Styles, startingPrice);
        await store.SaveArtistAsync(artist!, created, cancellationToken);
        return new(new ArtistProfile(artist!.Id, artist.Name, artist.Studio, artist.City, artist.Biography, artist.Styles, artist.StartingPrice), created);
    }
}
