using Microsoft.EntityFrameworkCore;

namespace InkLink.Catalog.Infrastructure.Persistence;

// Operational tool: caller supplies the catalog migration/owner credential, never an HTTP endpoint.
public sealed class CatalogSearchMaintenance(CatalogDbContext database)
{
    public async Task<int> RebuildAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.Database.ExecuteSqlRawAsync("LOCK TABLE catalog.artists IN SHARE ROW EXCLUSIVE MODE", cancellationToken);
        await database.SearchTerms.ExecuteDeleteAsync(cancellationToken);
        Guid? last = null;
        var count = 0;
        while (true)
        {
            var query = database.Artists.AsTracking();
            if (last.HasValue) query = query.Where(artist => artist.Id.CompareTo(last.Value) > 0);
            var batch = await query.OrderBy(artist => artist.Id).Take(200).ToListAsync(cancellationToken);
            if (batch.Count == 0) break;
            foreach (var artist in batch) SearchProjection.Update(database, artist);
            await database.SaveChangesAsync(cancellationToken);
            count += batch.Count;
            last = batch[^1].Id;
            database.ChangeTracker.Clear();
        }
        await transaction.CommitAsync(cancellationToken);
        return count;
    }
}
