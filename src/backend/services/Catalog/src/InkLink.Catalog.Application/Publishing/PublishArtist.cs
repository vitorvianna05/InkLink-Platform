using InkLink.Catalog.Application.Accounts;
using InkLink.Catalog.Application.Queries;
using InkLink.Catalog.Domain;

namespace InkLink.Catalog.Application.Publishing;

public sealed record ArtistInput(string? Name, string? Studio, string? City, string? Biography, string[]? Styles, decimal? StartingPrice);
/// <summary>Dados descritivos da obra; a imagem chega como arquivo (decisão 0018).</summary>
public sealed record ArtworkInput(string? Title, string? AlternativeText, string? Style, decimal? ReferencePrice);
/// <summary>Objetos confirmados por esta gravação e objetos que deixam de ser usados.</summary>
public sealed record ImageChange(IReadOnlyList<string> Confirmed, IReadOnlyList<string> Obsolete)
{
    public static readonly ImageChange None = new([], []);
}
public sealed record PublishedArtist(ArtistProfile Profile, bool Created);
public interface IArtistStore
{
    Task<Artist?> FindByOwnerAsync(Guid ownerId, CancellationToken cancellationToken);
    Task SaveArtistAsync(Artist artist, bool created, CancellationToken cancellationToken);
    Task<Artwork?> FindArtworkAsync(Guid artistId, Guid artworkId, ArtworkKind kind, CancellationToken cancellationToken);
    Task SaveArtworkAsync(Artwork artwork, bool created, ImageChange images, CancellationToken cancellationToken);
    Task DeleteArtworkAsync(Artwork artwork, IReadOnlyList<string> obsoleteObjects, CancellationToken cancellationToken);
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
