using Yarp.ReverseProxy.Configuration;

namespace InkLink.Gateway.Extensions;

internal static class IdentityRouteExtensions
{
    public static void AddIdentityRoutes(this ICollection<RouteConfig> routes)
    {
        routes.Add(new RouteConfig { RouteId = "identity-user", ClusterId = "identity", Match = new RouteMatch { Path = "/api/v1/profiles/{**remainder}" }, AuthorizationPolicy = "default" });
        // Confirmação do cadastro (decisão 0020).
        routes.Add(new RouteConfig { RouteId = "identity-registration", ClusterId = "identity", Match = new RouteMatch { Path = "/api/v1/registration/{**remainder}" }, AuthorizationPolicy = "default" });
        routes.Add(new RouteConfig { RouteId = "identity-technical", ClusterId = "identity", Match = new RouteMatch { Path = "/api/v1/internal/profiles/{**remainder}" }, AuthorizationPolicy = "default" });
    }
}
