using System.Security.Claims;
using InkLink.Catalog.Application.Queries;

namespace InkLink.Catalog.Api.Extensions;

internal static class EngagementContextEndpointExtensions
{
    public const string Policy = "catalog-engagement-context-reader";

    public static IServiceCollection AddEngagementContextAuthorization(this IServiceCollection services) => services.AddAuthorization(options =>
        options.AddPolicy(Policy, policy => policy.RequireAuthenticatedUser().RequireAssertion(context =>
            context.User.FindFirstValue("inklink_actor") == "service"
            && context.User.FindFirstValue("azp") == "inklink-engagements"
            && (context.User.FindFirstValue("scope") ?? "").Split(' ').Contains("catalog.engagement-context.read", StringComparer.Ordinal))));

    public static IEndpointRouteBuilder MapEngagementContext(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/internal/artists/{artistId:guid}/engagement-context", (Guid artistId, Guid? flashId, HttpContext http, IEngagementContextReader reader, CancellationToken cancellationToken) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            return reader.ReadAsync(artistId, flashId, cancellationToken);
        })
            .RequireAuthorization(Policy).WithTags("Internal engagement context").Produces<EngagementContext>()
            .ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).WithCatalogErrors();
        return endpoints;
    }
}
