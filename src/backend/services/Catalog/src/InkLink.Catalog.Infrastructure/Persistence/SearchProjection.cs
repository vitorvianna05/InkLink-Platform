using InkLink.Catalog.Domain;

namespace InkLink.Catalog.Infrastructure.Persistence;

internal static class SearchProjection
{
    public static void Update(CatalogDbContext database, Artist artist)
    {
        var entry = database.Entry(artist);
        entry.Property("NormalizedName").CurrentValue = SearchText.Normalize(artist.Name);
        entry.Property("NormalizedCity").CurrentValue = SearchText.Normalize(artist.City);
        entry.Property("NormalizedStyles").CurrentValue = artist.Styles.Select(SearchText.Normalize).ToArray();
        var terms = new[] { ("artist", artist.Name), ("studio", artist.Studio), ("city", artist.City) }.Concat(artist.Styles.Select(style => ("style", style)));
        database.SearchTerms.AddRange(terms.Select(term => new SearchTerm { ArtistId = artist.Id, Category = term.Item1, Value = term.Item2, Normalized = SearchText.Normalize(term.Item2) }));
    }
}
