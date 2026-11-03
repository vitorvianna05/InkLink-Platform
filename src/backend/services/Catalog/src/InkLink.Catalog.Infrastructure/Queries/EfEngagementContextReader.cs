using System.Data;
using InkLink.Catalog.Application;
using InkLink.Catalog.Application.Queries;
using InkLink.Catalog.Domain;
using InkLink.Catalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InkLink.Catalog.Infrastructure.Queries;

public sealed class EfEngagementContextReader(CatalogDbContext database) : IEngagementContextReader
{
    public async Task<EngagementContext> ReadAsync(Guid artistId, Guid? flashId, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var artist = await database.Artists.AsNoTracking().SingleOrDefaultAsync(a => a.Id == artistId, cancellationToken)
            ?? throw new CatalogFailure(CatalogError.NotFound, "catalog.artist_not_found");
        EngagementFlash? flash = null;
        if (flashId.HasValue)
        {
            var artwork = await database.Artworks.AsNoTracking().SingleOrDefaultAsync(a => a.Id == flashId && a.ArtistId == artistId && a.Kind == ArtworkKind.Flash, cancellationToken)
                ?? throw new CatalogFailure(CatalogError.NotFound, "catalog.flash_not_found");
            flash = new(artwork.Id, artwork.Title, artwork.ImageUrl, artwork.AlternativeText, artwork.Style, artwork.ReferencePrice);
        }
        return new(artist.Id, artist.OwnerId, artist.Name, artist.Studio, artist.City, artist.StartingPrice, flash);
    }
}
