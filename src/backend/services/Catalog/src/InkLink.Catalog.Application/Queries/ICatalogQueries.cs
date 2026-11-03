namespace InkLink.Catalog.Application.Queries;

public sealed record SearchCriteria(string Term, string Style, string City, decimal? MaxPrice, int Limit);
public sealed record QuerySlice<T>(IReadOnlyList<T> Items, PagePosition? Next, int Total);

public interface ICatalogQueries
{
    Task<ArtworkView?> FindArtworkAsync(Guid artistId, Guid artworkId, bool flashes, CancellationToken cancellationToken);
    Task<ArtistProfile?> FindArtistAsync(Guid id, CancellationToken cancellationToken);
    Task<QuerySlice<ArtistCard>> SearchAsync(SearchCriteria criteria, PagePosition? after, CancellationToken cancellationToken);
    Task<IReadOnlyList<Suggestion>> SuggestAsync(string term, int limit, CancellationToken cancellationToken);
    Task<QuerySlice<ArtworkView>?> ArtworksAsync(Guid artistId, bool flashes, int limit, PagePosition? after, CancellationToken cancellationToken);
}
