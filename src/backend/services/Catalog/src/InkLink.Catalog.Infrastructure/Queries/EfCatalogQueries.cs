using InkLink.Catalog.Application.Queries;
using InkLink.Catalog.Domain;
using InkLink.Catalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InkLink.Catalog.Infrastructure.Queries;

internal sealed class EfCatalogQueries(CatalogDbContext database) : ICatalogQueries
{
    public Task<ArtistProfile?> FindArtistAsync(Guid id, CancellationToken cancellationToken) => database.Artists.AsNoTracking()
        .Where(artist => artist.Id == id).Select(artist => new ArtistProfile(artist.Id, artist.Name, artist.Studio, artist.City, artist.Biography, artist.Styles, artist.StartingPrice))
        .SingleOrDefaultAsync(cancellationToken);

    public async Task<QuerySlice<ArtistCard>> SearchAsync(SearchCriteria criteria, PagePosition? after, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, cancellationToken);
        await database.Database.ExecuteSqlRawAsync("SET LOCAL pg_trgm.similarity_threshold = 0.3", cancellationToken);
        var artists = database.Artists.AsNoTracking();
        if (criteria.City.Length > 0) artists = artists.Where(artist => EF.Property<string>(artist, "NormalizedCity") == criteria.City);
        if (criteria.Style.Length > 0) artists = artists.Where(artist => EF.Property<string[]>(artist, "NormalizedStyles").Contains(criteria.Style));
        if (criteria.MaxPrice.HasValue) artists = artists.Where(artist => artist.StartingPrice <= criteria.MaxPrice.Value);
        IQueryable<RankedArtist> ranked;
        if (criteria.Term.Length == 0) ranked = artists.Select(artist => new RankedArtist { Artist = artist, Name = EF.Property<string>(artist, "NormalizedName"), Rank = 0 });
        else
        {
            var term = criteria.Term;
            var matches = MatchTerms(term).GroupBy(value => value.ArtistId)
                .Select(group => new { ArtistId = group.Key, Rank = group.Max(value => value.Rank) });
            ranked = from artist in artists
                     join match in matches on artist.Id equals match.ArtistId
                     select new RankedArtist { Artist = artist, Name = EF.Property<string>(artist, "NormalizedName"), Rank = match.Rank };
        }
        var total = await ranked.CountAsync(cancellationToken);
        if (after is not null) ranked = ranked.Where(value => value.Rank < after.Rank || (value.Rank == after.Rank
            && (string.Compare(value.Name, after.Name) > 0 || (value.Name == after.Name && value.Artist.Id.CompareTo(after.Id) > 0))));
        var rows = await ranked.OrderByDescending(value => value.Rank).ThenBy(value => value.Name).ThenBy(value => value.Artist.Id)
            .Take(criteria.Limit + 1).Select(value => new ArtistRow(value.Artist.Id, value.Artist.Name, value.Artist.Studio, value.Artist.City,
                value.Artist.Styles, value.Artist.StartingPrice, value.Name, value.Rank)).ToListAsync(cancellationToken);
        var page = rows.Take(criteria.Limit).ToArray();
        var next = rows.Count > criteria.Limit ? new PagePosition(page[^1].Rank, page[^1].NormalizedName, page[^1].Id) : null;
        await transaction.CommitAsync(cancellationToken);
        return new(page.Select(value => new ArtistCard(value.Id, value.Name, value.Studio, value.City, value.Styles, value.StartingPrice)).ToArray(), next, total);
    }

    public async Task<IReadOnlyList<Suggestion>> SuggestAsync(string term, int limit, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.Database.ExecuteSqlRawAsync("SET LOCAL pg_trgm.similarity_threshold = 0.3", cancellationToken);
        var results = await MatchTerms(term).GroupBy(value => new { value.Category, value.Normalized })
            .Select(group => new { group.Key.Category, group.Key.Normalized, Value = group.Min(value => value.Value)!, Rank = group.Max(value => value.Rank) })
            .OrderByDescending(value => value.Rank).ThenBy(value => value.Category).ThenBy(value => value.Normalized).Take(limit).ToListAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return results.Select(value => new Suggestion(value.Category, value.Value, value.Rank)).ToArray();
    }

    private IQueryable<RankedTerm> MatchTerms(string term) => database.SearchTerms.AsNoTracking()
        .Where(value => value.Normalized.StartsWith(term) || (term.Length >= 3 && EF.Functions.TrigramsAreSimilar(value.Normalized, term)))
        .Select(value => new RankedTerm
        {
            ArtistId = value.ArtistId,
            Category = value.Category,
            Value = value.Value,
            Normalized = value.Normalized,
            Rank = value.Normalized == term ? 3 : value.Normalized.StartsWith(term) ? 2 : EF.Functions.TrigramsWordSimilarity(term, value.Normalized)
        });

    public async Task<QuerySlice<ArtworkView>?> ArtworksAsync(Guid artistId, bool flashes, int limit, PagePosition? after, CancellationToken cancellationToken)
    {
        if (!await database.Artists.AnyAsync(artist => artist.Id == artistId, cancellationToken)) return null;
        var kind = flashes ? ArtworkKind.Flash : ArtworkKind.Tattoo;
        var query = database.Artworks.AsNoTracking().Where(artwork => artwork.ArtistId == artistId && artwork.Kind == kind);
        var total = await query.CountAsync(cancellationToken);
        if (after is not null) query = query.Where(artwork => artwork.Id.CompareTo(after.Id) > 0);
        var rows = await query.OrderBy(artwork => artwork.Id).Take(limit + 1)
            .Select(artwork => new ArtworkView(artwork.Id, artwork.Title, artwork.ImageUrl, artwork.AlternativeText, artwork.Style, artwork.ReferencePrice)).ToListAsync(cancellationToken);
        var page = rows.Take(limit).ToArray();
        return new(page, rows.Count > limit ? new PagePosition(0, "", page[^1].Id) : null, total);
    }

    public Task<ArtworkView?> FindArtworkAsync(Guid artistId, Guid artworkId, bool flashes, CancellationToken cancellationToken) =>
        database.Artworks.AsNoTracking().Where(artwork => artwork.Id == artworkId && artwork.ArtistId == artistId && artwork.Kind == (flashes ? ArtworkKind.Flash : ArtworkKind.Tattoo))
            .Select(artwork => new ArtworkView(artwork.Id, artwork.Title, artwork.ImageUrl, artwork.AlternativeText, artwork.Style, artwork.ReferencePrice)).SingleOrDefaultAsync(cancellationToken);

    private sealed class RankedArtist
    {
        public Artist Artist { get; init; } = null!;
        public string Name { get; init; } = string.Empty;
        public double Rank { get; init; }
    }
    private sealed class RankedTerm
    {
        public Guid ArtistId { get; init; }
        public string Category { get; init; } = string.Empty;
        public string Value { get; init; } = string.Empty;
        public string Normalized { get; init; } = string.Empty;
        public double Rank { get; init; }
    }
    private sealed record ArtistRow(Guid Id, string Name, string Studio, string City, string[] Styles, decimal StartingPrice, string NormalizedName, double Rank);
}
