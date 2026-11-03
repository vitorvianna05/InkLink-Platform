using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;

namespace InkLink.Gateway.Extensions;

internal static class GatewayProxyExtensions
{
    public static IServiceCollection AddBackendProxy(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var routes = new List<RouteConfig>();
        var clusters = new List<ClusterConfig>();
        routes.AddIdentityRoutes();
        routes.AddCatalogRoutes();
        clusters.AddBackendCluster("identity", configuration["Gateway:IdentityAddress"], environment);
        clusters.AddBackendCluster("catalog", configuration["Gateway:CatalogAddress"], environment);
        if (!string.IsNullOrWhiteSpace(configuration["Gateway:EngagementsAddress"]))
        {
            routes.Add(new RouteConfig { RouteId = "engagements", ClusterId = "engagements", Match = new RouteMatch { Path = "/api/v1/engagements/{**remainder}" }, AuthorizationPolicy = "default" });
            routes.Add(new RouteConfig { RouteId = "engagements-v2", ClusterId = "engagements", Match = new RouteMatch { Path = "/api/v2/engagements/{**remainder}" }, AuthorizationPolicy = "default" });
            clusters.AddBackendCluster("engagements", configuration["Gateway:EngagementsAddress"], environment);
        }
        if (!string.IsNullOrWhiteSpace(configuration["Gateway:MessagingAddress"]))
        {
            routes.Add(new RouteConfig { RouteId = "conversations", ClusterId = "messaging", Match = new RouteMatch { Path = "/api/v1/conversations/{**remainder}" }, AuthorizationPolicy = "default" });
            clusters.AddBackendCluster("messaging", configuration["Gateway:MessagingAddress"], environment);
        }
        if (!string.IsNullOrWhiteSpace(configuration["Gateway:SchedulingAddress"]))
        {
            routes.Add(new RouteConfig { RouteId = "scheduling", ClusterId = "scheduling", Match = new RouteMatch { Path = "/api/v1/scheduling/{**remainder}" }, AuthorizationPolicy = "default" });
            clusters.AddBackendCluster("scheduling", configuration["Gateway:SchedulingAddress"], environment);
        }
        services.AddReverseProxy().LoadFromMemory(routes, clusters)
            .AddTransforms(context =>
            {
                context.AddRequestHeaderRemove("Cookie");
                context.AddRequestHeaderRemove("X-User-Id");
                context.AddRequestHeaderRemove("X-Roles");
            });
        return services;
    }
}
