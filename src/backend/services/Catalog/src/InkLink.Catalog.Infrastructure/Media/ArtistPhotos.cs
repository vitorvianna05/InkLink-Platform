using InkLink.Catalog.Application.Profiles;
using InkLink.Catalog.Application.Queries;
using InkLink.Catalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InkLink.Catalog.Infrastructure.Media;

/// <summary>
/// Projeção da foto pública do artista (decisão 0018), alimentada por identity.profile-photo-changed.v1.
/// A revisão crescente da Identity descarta eventos repetidos ou fora de ordem.
/// </summary>
public sealed class ArtistPhoto
{
    public Guid OwnerId { get; set; }
    public string? PhotoUrl { get; set; }
    public string? ThumbnailUrl { get; set; }
    public int Revision { get; set; }
}

/// <summary>Aplica as fotos projetadas a cards e perfis já carregados.</summary>
public sealed class ArtistPhotoLookup(CatalogDbContext database)
{
    public async Task<IReadOnlyDictionary<Guid, ArtistPhoto>> ForArtistsAsync(IReadOnlyCollection<Guid> artistIds, CancellationToken cancellationToken)
    {
        if (artistIds.Count == 0) return new Dictionary<Guid, ArtistPhoto>();
        var rows = await (from artist in database.Artists.AsNoTracking()
                          join photo in database.ArtistPhotos.AsNoTracking() on artist.OwnerId equals photo.OwnerId
                          where artistIds.Contains(artist.Id) && photo.PhotoUrl != null
                          select new { artist.Id, Photo = photo }).ToListAsync(cancellationToken);
        return rows.ToDictionary(row => row.Id, row => row.Photo);
    }

    public async Task<IReadOnlyList<ArtistCard>> ApplyAsync(IReadOnlyList<ArtistCard> cards, CancellationToken cancellationToken)
    {
        var photos = await ForArtistsAsync(cards.Select(card => card.Id).ToArray(), cancellationToken);
        return cards.Select(card => photos.TryGetValue(card.Id, out var photo) ? card with { PhotoUrl = photo.PhotoUrl, PhotoThumbnailUrl = photo.ThumbnailUrl } : card).ToArray();
    }
}

/// <summary>Grava a foto projetada somente quando a revisão é mais nova que a atual.</summary>
internal sealed class EfArtistPhotoProjection(CatalogDbContext database) : IArtistPhotoProjection
{
    public async Task ApplyAsync(ArtistPhotoChange change, CancellationToken cancellationToken) =>
        await database.Database.ExecuteSqlAsync(
            $"INSERT INTO catalog.artist_photos (\"OwnerId\", \"PhotoUrl\", \"ThumbnailUrl\", \"Revision\") VALUES ({change.OwnerId}, {change.PhotoUrl}, {change.ThumbnailUrl}, {change.Revision}) ON CONFLICT (\"OwnerId\") DO UPDATE SET \"PhotoUrl\" = EXCLUDED.\"PhotoUrl\", \"ThumbnailUrl\" = EXCLUDED.\"ThumbnailUrl\", \"Revision\" = EXCLUDED.\"Revision\" WHERE catalog.artist_photos.\"Revision\" < EXCLUDED.\"Revision\"",
            cancellationToken);
}
