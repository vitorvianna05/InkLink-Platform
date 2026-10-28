using Microsoft.AspNetCore.Authentication;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;

namespace InkLink.Bff.Extensions;

internal static class BffProxyExtensions
{
    public static IServiceCollection AddGatewayProxy(this IServiceCollection services, BffSettings settings)
    {
        services.AddReverseProxy().LoadFromMemory(
            [new RouteConfig { RouteId = "web-api", ClusterId = "gateway", Match = new RouteMatch { Path = "/api/v1/{**remainder}" }, AuthorizationPolicy = "default" }],
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
                    transform.ProxyRequest.Headers.Authorization = new("Bearer", token);
                });
            });
        return services;
    }
}
