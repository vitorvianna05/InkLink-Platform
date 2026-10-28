using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;

namespace InkLink.Gateway.Extensions;

internal static class GatewayProxyExtensions
{
    public static IServiceCollection AddIdentityProxy(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var destination = configuration["Gateway:IdentityAddress"];
        if (!Uri.TryCreate(destination, UriKind.Absolute, out var target) || (target.Scheme is not ("https" or "http")) || (target.Scheme != "https" && !environment.IsDevelopment() && !environment.IsEnvironment("Testing")))
            throw new InvalidOperationException("Gateway requires a valid Identity address; HTTPS is mandatory outside local/test environments.");
        services.AddReverseProxy().LoadFromMemory(
            [new RouteConfig { RouteId = "identity-user", ClusterId = "identity", Match = new RouteMatch { Path = "/api/v1/profiles/{**remainder}" }, AuthorizationPolicy = "default" },
             new RouteConfig { RouteId = "identity-technical", ClusterId = "identity", Match = new RouteMatch { Path = "/api/v1/internal/profiles/{**remainder}" }, AuthorizationPolicy = "default" }],
            [new ClusterConfig { ClusterId = "identity", Destinations = new Dictionary<string, DestinationConfig> { ["identity"] = new() { Address = destination! } },
                HttpRequest = new() { ActivityTimeout = TimeSpan.FromSeconds(30) } }])
            .AddTransforms(context =>
            {
                context.AddRequestHeaderRemove("Cookie");
                context.AddRequestHeaderRemove("X-User-Id");
                context.AddRequestHeaderRemove("X-Roles");
            });
        return services;
    }
}
