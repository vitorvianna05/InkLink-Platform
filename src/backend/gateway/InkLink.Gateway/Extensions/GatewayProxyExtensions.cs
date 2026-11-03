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
