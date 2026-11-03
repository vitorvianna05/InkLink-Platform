using InkLink.Catalog.Application.Favorites;
using InkLink.Catalog.Application.Queries;

namespace InkLink.Catalog.Api.Extensions;

internal static class FavoriteEndpointExtensions
{
    public static IEndpointRouteBuilder MapCatalogFavorites(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/favorites", (ManageFavorites favorites, CancellationToken cancellationToken, int limit = 20, string? cursor = null) => favorites.ListAsync(limit, cursor, cancellationToken))
            .WithTags("Favorites").RequireCatalogAccount().Produces<Page<ArtistCard>>().WithFavoriteErrors();
        endpoints.MapPut("/api/v1/favorites/{artistId:guid}", async (Guid artistId, ManageFavorites favorites, CancellationToken cancellationToken) =>
        {
            await favorites.AddAsync(artistId, cancellationToken);
            return Results.NoContent();
        }).WithTags("Favorites").RequireCatalogAccount().Produces(204).ProducesProblem(404).WithFavoriteErrors();
        endpoints.MapDelete("/api/v1/favorites/{artistId:guid}", async (Guid artistId, ManageFavorites favorites, CancellationToken cancellationToken) =>
        {
            await favorites.RemoveAsync(artistId, cancellationToken);
            return Results.NoContent();
        }).WithTags("Favorites").RequireCatalogAccount().Produces(204).WithFavoriteErrors();
        return endpoints;
    }

    private static RouteHandlerBuilder WithFavoriteErrors(this RouteHandlerBuilder endpoint) =>
        endpoint.ProducesProblem(401).ProducesProblem(403).WithCatalogErrors();
}
