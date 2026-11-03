using System.Security.Claims;
using InkLink.Engagements.Application.Scheduling;

namespace InkLink.Engagements.Api.Extensions;

internal static class SchedulingContextExtensions
{
    private const string Policy = "scheduling-context-reader";
    public static IServiceCollection AddSchedulingContext(this IServiceCollection services)
    {
        services.AddScoped<ReadSchedulingContext>();
        services.AddAuthorization(options => options.AddPolicy(Policy, policy => policy.RequireAuthenticatedUser().RequireAssertion(c =>
            c.User.FindFirstValue("inklink_actor") == "service" && c.User.FindFirstValue("azp") == "inklink-scheduling"
            && (c.User.FindFirstValue("scope") ?? "").Split(' ').Contains("engagements.scheduling-context.read", StringComparer.Ordinal))));
        return services;
    }
    public static IEndpointRouteBuilder MapSchedulingContext(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/internal/engagements/{id:guid}/scheduling-context", (Guid id, ReadSchedulingContext read, HttpContext http, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store"; return read.ExecuteAsync(id, ct);
        }).RequireAuthorization(Policy).Produces<SchedulingContext>().ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(500).ProducesProblem(503);
        return endpoints;
    }
}
