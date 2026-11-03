namespace InkLink.Catalog.Application.Queries;

public sealed record ArtistCard(Guid Id, string Name, string Studio, string City, string[] Styles, decimal StartingPrice);
public sealed record ArtistProfile(Guid Id, string Name, string Studio, string City, string Biography, string[] Styles, decimal StartingPrice);
public sealed record ArtworkView(Guid Id, string Title, string ImageUrl, string AlternativeText, string Style, decimal? ReferencePrice);
public sealed record Page<T>(IReadOnlyList<T> Items, string? NextCursor, bool HasMore, int Total);
public sealed record Suggestion(string Category, string Value, double Relevance);
public sealed record ArtistSearch(string? Term = null, string? Style = null, string? City = null, decimal? MaxPrice = null, int Limit = 20, string? Cursor = null);
