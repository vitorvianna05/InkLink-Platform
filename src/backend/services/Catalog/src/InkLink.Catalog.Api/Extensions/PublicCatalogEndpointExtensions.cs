using InkLink.Catalog.Application.Queries;

namespace InkLink.Catalog.Api.Extensions;

internal static class PublicCatalogEndpointExtensions
{
    public static IEndpointRouteBuilder MapPublicCatalog(this IEndpointRouteBuilder endpoints)
    {
        var artists = endpoints.MapGroup("/api/v1/artists").WithTags("Public catalog");
        artists.MapGet("", (ReadCatalog read, CancellationToken cancellationToken, int limit = 20, string? cursor = null) =>
            read.SearchAsync(new ArtistSearch(Limit: limit, Cursor: cursor), cancellationToken))
            .Produces<Page<ArtistCard>>().WithCatalogErrors();
        artists.MapGet("/search", (ReadCatalog read, CancellationToken cancellationToken, string? term = null, string? style = null, string? city = null, decimal? maxPrice = null, int limit = 20, string? cursor = null) =>
            read.SearchAsync(new ArtistSearch(term, style, city, maxPrice, limit, cursor), cancellationToken))
            .Produces<Page<ArtistCard>>().WithCatalogErrors();
        artists.MapGet("/suggestions", (ReadCatalog read, CancellationToken cancellationToken, string? term = null, int limit = 10) => read.SuggestAsync(term, limit, cancellationToken))
            .Produces<IReadOnlyList<Suggestion>>().WithCatalogErrors();
        artists.MapGet("/{id:guid}", (Guid id, ReadCatalog read, CancellationToken cancellationToken) => read.ArtistAsync(id, cancellationToken))
            .Produces<ArtistProfile>().ProducesProblem(404).WithCatalogErrors();
        foreach (var flashes in new[] { false, true })
        {
            var kind = flashes ? "flashes" : "tattoos";
            artists.MapGet("/{id:guid}/" + kind, (Guid id, ReadCatalog read, CancellationToken cancellationToken, int limit = 20, string? cursor = null) => read.ArtworksAsync(id, flashes, limit, cursor, cancellationToken))
                .Produces<Page<ArtworkView>>().ProducesProblem(404).WithCatalogErrors();
            artists.MapGet("/{id:guid}/" + kind + "/{artworkId:guid}", (Guid id, Guid artworkId, ReadCatalog read, CancellationToken cancellationToken) => read.ArtworkAsync(id, artworkId, flashes, cancellationToken))
                .Produces<ArtworkView>().ProducesProblem(404).WithCatalogErrors();
        }
        return endpoints;
    }

    public static RouteHandlerBuilder WithCatalogErrors(this RouteHandlerBuilder endpoint) =>
        endpoint.ProducesValidationProblem().ProducesProblem(500).ProducesProblem(503);
}
