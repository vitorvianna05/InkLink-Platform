using Yarp.ReverseProxy.Configuration;

namespace InkLink.Gateway.Extensions;

internal static class BackendClusterExtensions
{
    public static void AddBackendCluster(this ICollection<ClusterConfig> clusters, string name, string? address, IHostEnvironment environment)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var target) || target.Scheme is not ("http" or "https")
            || (target.Scheme != "https" && !environment.IsDevelopment() && !environment.IsEnvironment("Testing")))
            throw new InvalidOperationException("Gateway requires valid backend addresses; HTTPS is mandatory outside local/test environments.");
        clusters.Add(new ClusterConfig { ClusterId = name, Destinations = new Dictionary<string, DestinationConfig> { [name] = new() { Address = address! } }, HttpRequest = new() { ActivityTimeout = TimeSpan.FromSeconds(30) } });
    }
}
