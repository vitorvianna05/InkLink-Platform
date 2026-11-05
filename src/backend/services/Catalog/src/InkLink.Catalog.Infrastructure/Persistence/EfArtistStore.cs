using InkLink.Catalog.Application;
using InkLink.Catalog.Application.Publishing;
using InkLink.Catalog.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace InkLink.Catalog.Infrastructure.Persistence;

internal sealed class EfArtistStore(CatalogDbContext database, TimeProvider time) : IArtistStore
{
    public Task<Artist?> FindByOwnerAsync(Guid ownerId, CancellationToken cancellationToken) => database.Artists.SingleOrDefaultAsync(artist => artist.OwnerId == ownerId, cancellationToken);

    public async Task SaveArtistAsync(Artist artist, bool created, CancellationToken cancellationToken)
    {
        // Serialize replacement of the search projection with its source, using a local transaction.
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (created) database.Artists.Add(artist);
            else
            {
                await database.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM catalog.artists WHERE \"Id\" = {artist.Id} FOR UPDATE", cancellationToken);
                // PUT replaces the complete representation, including fields equal to an earlier stale read.
                database.Entry(artist).State = EntityState.Modified;
            }
            if (!created) await database.SearchTerms.Where(term => term.ArtistId == artist.Id).ExecuteDeleteAsync(cancellationToken);
            SearchProjection.Update(database, artist);
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_artists_OwnerId" })
        {
            throw new CatalogFailure(CatalogError.Conflict, "catalog.publication_conflict");
        }
    }

    public Task<Artwork?> FindArtworkAsync(Guid artistId, Guid artworkId, ArtworkKind kind, CancellationToken cancellationToken) =>
        database.Artworks.SingleOrDefaultAsync(artwork => artwork.Id == artworkId && artwork.ArtistId == artistId && artwork.Kind == kind, cancellationToken);

    public async Task SaveArtworkAsync(Artwork artwork, bool created, ImageChange images, CancellationToken cancellationToken)
    {
        // A obra, a confirmação dos objetos novos e o agendamento da remoção dos antigos são atômicos (decisão 0018).
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        if (created) database.Artworks.Add(artwork);
        else database.Entry(artwork).State = EntityState.Modified;
        await database.SaveChangesAsync(cancellationToken);
        await ScheduleAsync(images, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteArtworkAsync(Artwork artwork, IReadOnlyList<string> obsoleteObjects, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        database.Artworks.Remove(artwork);
        await database.SaveChangesAsync(cancellationToken);
        await ScheduleAsync(new ImageChange([], obsoleteObjects), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task ScheduleAsync(ImageChange images, CancellationToken cancellationToken)
    {
        if (images.Confirmed.Count > 0)
            await database.ObjectCleanups.Where(item => images.Confirmed.Contains(item.Key)).ExecuteDeleteAsync(cancellationToken);
        if (images.Obsolete.Count == 0) return;
        var now = time.GetUtcNow();
        foreach (var key in images.Obsolete)
            await database.Database.ExecuteSqlAsync(
                $"INSERT INTO catalog.object_cleanups (\"Id\", \"Key\", \"DueAt\", \"Attempts\") VALUES ({Guid.CreateVersion7()}, {key}, {now}, 0) ON CONFLICT (\"Key\") DO UPDATE SET \"DueAt\" = EXCLUDED.\"DueAt\"",
                cancellationToken);
    }
}
