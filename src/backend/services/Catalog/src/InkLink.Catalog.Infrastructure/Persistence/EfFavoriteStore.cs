using InkLink.Catalog.Application.Favorites;
using InkLink.Catalog.Application.Queries;
using InkLink.Catalog.Domain;
using Microsoft.EntityFrameworkCore;

namespace InkLink.Catalog.Infrastructure.Persistence;

internal sealed class EfFavoriteStore(CatalogDbContext database) : IFavoriteStore
{
    public async Task<bool> AddAsync(Guid clientId, Guid artistId, CancellationToken cancellationToken)
    {
        var favorite = Favorite.Create(clientId, artistId);
        var count = await database.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO catalog.favorites (\"ClientId\", \"ArtistId\") SELECT {favorite.ClientId}, \"Id\" FROM catalog.artists WHERE \"Id\" = {favorite.ArtistId} ON CONFLICT DO NOTHING", cancellationToken);
        return count > 0 || await database.Artists.AnyAsync(artist => artist.Id == artistId, cancellationToken);
    }

    public async Task RemoveAsync(Guid clientId, Guid artistId, CancellationToken cancellationToken) =>
        _ = await database.Favorites.Where(favorite => favorite.ClientId == clientId && favorite.ArtistId == artistId).ExecuteDeleteAsync(cancellationToken);

    public async Task<QuerySlice<ArtistCard>> ListAsync(Guid clientId, int limit, PagePosition? after, CancellationToken cancellationToken)
    {
        var query = from favorite in database.Favorites.AsNoTracking()
                    join artist in database.Artists.AsNoTracking() on favorite.ArtistId equals artist.Id
                    where favorite.ClientId == clientId
                    select artist;
        var total = await query.CountAsync(cancellationToken);
        if (after is not null) query = query.Where(artist => artist.Id.CompareTo(after.Id) > 0);
        var rows = await query.OrderBy(artist => artist.Id).Take(limit + 1)
            .Select(artist => new ArtistCard(artist.Id, artist.Name, artist.Studio, artist.City, artist.Styles, artist.StartingPrice)).ToListAsync(cancellationToken);
        var page = rows.Take(limit).ToArray();
        return new(page, rows.Count > limit ? new PagePosition(0, "", page[^1].Id) : null, total);
    }
}
