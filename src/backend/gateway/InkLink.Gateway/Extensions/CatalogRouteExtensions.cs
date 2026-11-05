using Yarp.ReverseProxy.Configuration;

namespace InkLink.Gateway.Extensions;

internal static class CatalogRouteExtensions
{
    public static void AddCatalogRoutes(this ICollection<RouteConfig> routes)
    {
        routes.Add(new RouteConfig { RouteId = "catalog-public", ClusterId = "catalog", Order = 0, Match = new RouteMatch { Path = "/api/v1/artists/{**remainder}", Methods = ["GET", "HEAD"] }, AuthorizationPolicy = "anonymous" });
        routes.Add(new RouteConfig { RouteId = "catalog-publication", ClusterId = "catalog", Order = 1, Match = new RouteMatch { Path = "/api/v1/artists/{**remainder}" }, AuthorizationPolicy = "default" });
        // Publicação de obras com upload de imagem (decisão 0018).
        routes.Add(new RouteConfig { RouteId = "catalog-publication-v2", ClusterId = "catalog", Order = 1, Match = new RouteMatch { Path = "/api/v2/artists/me/{**remainder}" }, AuthorizationPolicy = "default" });
        routes.Add(new RouteConfig { RouteId = "catalog-favorites", ClusterId = "catalog", Match = new RouteMatch { Path = "/api/v1/favorites/{**remainder}" }, AuthorizationPolicy = "default" });
    }
}
