namespace InkLink.Catalog.Application.Queries;

/// <summary>Card do artista; a foto pública vem da projeção alimentada pela Identity (decisão 0018).</summary>
public sealed record ArtistCard(Guid Id, string Name, string Studio, string City, string[] Styles, decimal StartingPrice, string? PhotoUrl = null, string? PhotoThumbnailUrl = null);
public sealed record ArtistProfile(Guid Id, string Name, string Studio, string City, string Biography, string[] Styles, decimal StartingPrice, string? PhotoUrl = null, string? PhotoThumbnailUrl = null);
/// <summary>
/// Obra publicada. <c>ImageUrl</c> é a variante de detalhe e <c>ThumbnailUrl</c> a de card; ambas apontam
/// para a URL externa legada quando <c>ExternalImage</c> é verdadeiro (decisão 0018).
/// </summary>
public sealed record ArtworkView(Guid Id, string Title, string ImageUrl, string AlternativeText, string Style, decimal? ReferencePrice, string ThumbnailUrl, bool ExternalImage);
public sealed record Page<T>(IReadOnlyList<T> Items, string? NextCursor, bool HasMore, int Total);
public sealed record Suggestion(string Category, string Value, double Relevance);
public sealed record ArtistSearch(string? Term = null, string? Style = null, string? City = null, decimal? MaxPrice = null, int Limit = 20, string? Cursor = null);
