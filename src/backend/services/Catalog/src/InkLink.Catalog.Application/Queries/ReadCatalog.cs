using System.Globalization;
using InkLink.Catalog.Domain;

namespace InkLink.Catalog.Application.Queries;

public sealed class ReadCatalog(ICatalogQueries queries)
{
    public async Task<ArtistProfile> ArtistAsync(Guid id, CancellationToken cancellationToken) =>
        await queries.FindArtistAsync(id, cancellationToken) ?? throw new CatalogFailure(CatalogError.NotFound, "catalog.artist_not_found");

    public async Task<Page<ArtistCard>> SearchAsync(ArtistSearch request, CancellationToken cancellationToken)
    {
        ValidateLimit(request.Limit);
        ValidateText(request.Term, 100, "term");
        ValidateText(request.Style, 80, "style");
        ValidateText(request.City, 100, "city");
        if (request.MaxPrice is < 0 or > 9999999.99m) throw CatalogFailure.Invalid("maxPrice", "Price must be between zero and 9999999.99.");
        var criteria = new SearchCriteria(SearchText.Normalize(request.Term), SearchText.Normalize(request.Style), SearchText.Normalize(request.City), request.MaxPrice, request.Limit);
        var fingerprint = CatalogCursor.Fingerprint(criteria.Term, criteria.Style, criteria.City, criteria.MaxPrice?.ToString(CultureInfo.InvariantCulture) ?? "");
        var after = CatalogCursor.Decode(request.Cursor, "artists", fingerprint);
        var result = await queries.SearchAsync(criteria, after, cancellationToken);
        return new(result.Items, result.Next is null ? null : CatalogCursor.Encode("artists", fingerprint, result.Next), result.Next is not null, result.Total);
    }

    public Task<IReadOnlyList<Suggestion>> SuggestAsync(string? term, int limit, CancellationToken cancellationToken)
    {
        ValidateLimit(limit);
        ValidateText(term, 100, "term");
        var normalized = SearchText.Normalize(term);
        return normalized.Length == 0 ? Task.FromResult<IReadOnlyList<Suggestion>>([]) : queries.SuggestAsync(normalized, limit, cancellationToken);
    }

    public async Task<Page<ArtworkView>> ArtworksAsync(Guid artistId, bool flashes, int limit, string? cursor, CancellationToken cancellationToken)
    {
        ValidateLimit(limit);
        var resource = flashes ? "flashes" : "tattoos";
        var criteria = artistId.ToString();
        var after = CatalogCursor.Decode(cursor, resource, criteria);
        var result = await queries.ArtworksAsync(artistId, flashes, limit, after, cancellationToken)
            ?? throw new CatalogFailure(CatalogError.NotFound, "catalog.artist_not_found");
        return new(result.Items, result.Next is null ? null : CatalogCursor.Encode(resource, criteria, result.Next), result.Next is not null, result.Total);
    }

    public async Task<ArtworkView> ArtworkAsync(Guid artistId, Guid artworkId, bool flashes, CancellationToken cancellationToken) =>
        await queries.FindArtworkAsync(artistId, artworkId, flashes, cancellationToken) ?? throw new CatalogFailure(CatalogError.NotFound, "catalog.artwork_not_found");

    public static void ValidateLimit(int limit)
    {
        if (limit is < 1 or > 100) throw CatalogFailure.Invalid("limit", "Limit must be between 1 and 100.");
    }

    private static void ValidateText(string? value, int length, string field)
    {
        if (value?.Length > length) throw CatalogFailure.Invalid(field, "Value is too long.");
    }
}
