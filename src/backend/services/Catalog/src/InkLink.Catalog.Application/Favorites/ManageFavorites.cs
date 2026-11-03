using InkLink.Catalog.Application.Accounts;
using InkLink.Catalog.Application.Queries;

namespace InkLink.Catalog.Application.Favorites;

public interface IFavoriteStore
{
    Task<bool> AddAsync(Guid clientId, Guid artistId, CancellationToken cancellationToken);
    Task RemoveAsync(Guid clientId, Guid artistId, CancellationToken cancellationToken);
    Task<QuerySlice<ArtistCard>> ListAsync(Guid clientId, int limit, PagePosition? after, CancellationToken cancellationToken);
}

public sealed class ManageFavorites(ICurrentCatalogAccount current, IFavoriteStore store)
{
    public async Task AddAsync(Guid artistId, CancellationToken cancellationToken)
    {
        var account = AccountAccess.Require(current);
        if (!await store.AddAsync(account.Id, artistId, cancellationToken)) throw new CatalogFailure(CatalogError.NotFound, "catalog.artist_not_found");
    }

    public Task RemoveAsync(Guid artistId, CancellationToken cancellationToken) => store.RemoveAsync(AccountAccess.Require(current).Id, artistId, cancellationToken);

    public async Task<Page<ArtistCard>> ListAsync(int limit, string? cursor, CancellationToken cancellationToken)
    {
        var account = AccountAccess.Require(current);
        ReadCatalog.ValidateLimit(limit);
        var fingerprint = account.Id.ToString();
        var after = CatalogCursor.Decode(cursor, "favorites", fingerprint);
        var result = await store.ListAsync(account.Id, limit, after, cancellationToken);
        return new(result.Items, result.Next is null ? null : CatalogCursor.Encode("favorites", fingerprint, result.Next), result.Next is not null, result.Total);
    }
}
