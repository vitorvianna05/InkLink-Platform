using InkLink.Catalog.Application.Publishing;
using InkLink.Catalog.Application.Queries;
using InkLink.Catalog.Domain;

namespace InkLink.Catalog.Api.Extensions;

internal static class CatalogPublishingEndpointExtensions
{
    public static IEndpointRouteBuilder MapCatalogPublishing(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("/api/v1/artists/me", async (ArtistInput input, PublishArtist publish, CancellationToken cancellationToken) =>
        {
            var result = await publish.ExecuteAsync(input, cancellationToken);
            return result.Created ? Results.Created("/api/v1/artists/" + result.Profile.Id, result.Profile) : Results.Ok(result.Profile);
        }).WithTags("Publication").RequireCatalogAccount().Produces<ArtistProfile>().Produces<ArtistProfile>(201).WithPublishingErrors();
        foreach (var kind in new[] { ArtworkKind.Tattoo, ArtworkKind.Flash })
        {
            var segment = kind == ArtworkKind.Flash ? "flashes" : "tattoos";
            var route = "/api/v1/artists/me/" + segment;
            endpoints.MapPost(route, async (ArtworkInput input, ManageArtworks manage, CancellationToken cancellationToken) =>
            {
                var result = await manage.CreateAsync(kind, input, cancellationToken);
                return Results.Created($"/api/v1/artists/{result.ArtistId}/{segment}/{result.Artwork.Id}", result.Artwork);
            }).WithTags("Publication").RequireCatalogAccount().Produces<ArtworkView>(201).WithPublishingErrors();
            endpoints.MapPut(route + "/{id:guid}", async (Guid id, ArtworkInput input, ManageArtworks manage, CancellationToken cancellationToken) =>
            {
                await manage.UpdateAsync(id, kind, input, cancellationToken);
                return Results.NoContent();
            }).WithTags("Publication").RequireCatalogAccount().Produces(204).WithPublishingErrors();
            endpoints.MapDelete(route + "/{id:guid}", async (Guid id, ManageArtworks manage, CancellationToken cancellationToken) =>
            {
                await manage.DeleteAsync(id, kind, cancellationToken);
                return Results.NoContent();
            }).WithTags("Publication").RequireCatalogAccount().Produces(204).WithPublishingErrors();
        }
        return endpoints;
    }

    private static RouteHandlerBuilder WithPublishingErrors(this RouteHandlerBuilder endpoint) =>
        endpoint.ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409).WithCatalogErrors();
}
