using Microsoft.AspNetCore.Authentication;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;

namespace InkLink.Bff.Extensions;

internal static class BffProxyExtensions
{
    public static IServiceCollection AddGatewayProxy(this IServiceCollection services, BffSettings settings)
    {
        services.AddReverseProxy().LoadFromMemory(
            [new RouteConfig { RouteId = "web-catalog-public", ClusterId = "gateway", Order = 0, Match = new RouteMatch { Path = "/api/v1/artists/{**remainder}", Methods = ["GET", "HEAD"] }, AuthorizationPolicy = "anonymous" },
             new RouteConfig { RouteId = "web-api", Order = 1, ClusterId = "gateway", Match = new RouteMatch { Path = "/api/v1/{**remainder}" }, AuthorizationPolicy = "default" },
             new RouteConfig { RouteId = "web-catalog-publication-v2", Order = 1, ClusterId = "gateway", Match = new RouteMatch { Path = "/api/v2/artists/me/{**remainder}" }, AuthorizationPolicy = "default" },
             new RouteConfig { RouteId = "web-engagements-v2", Order = 1, ClusterId = "gateway", Match = new RouteMatch { Path = "/api/v2/engagements/{**remainder}" }, AuthorizationPolicy = "default" }],
            [new ClusterConfig { ClusterId = "gateway", Destinations = new Dictionary<string, DestinationConfig> { ["gateway"] = new() { Address = settings.GatewayAddress } },
                HttpRequest = new() { ActivityTimeout = TimeSpan.FromSeconds(30) } }])
            .AddTransforms(context =>
            {
                context.AddRequestHeaderRemove("Cookie");
                context.AddRequestHeaderRemove("X-CSRF-TOKEN");
                context.AddRequestHeaderRemove("X-User-Id");
                context.AddRequestHeaderRemove("X-Roles");
                context.AddRequestTransform(async transform =>
                {
                    var token = await transform.HttpContext.GetTokenAsync("session", "access_token");
                    transform.ProxyRequest.Headers.Authorization = string.IsNullOrEmpty(token) ? null : new("Bearer", token);
                });
            });
        return services;
    }
}
