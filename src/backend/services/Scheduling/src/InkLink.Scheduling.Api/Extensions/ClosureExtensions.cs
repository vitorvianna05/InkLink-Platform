using System.Security.Claims;
using InkLink.Scheduling.Application.Reservations;
using InkLink.Scheduling.Infrastructure.Persistence;
namespace InkLink.Scheduling.Api.Extensions;

internal static class ClosureExtensions
{
    public static IServiceCollection AddSchedulingClosure(this IServiceCollection services)
    {
        services.AddScoped<IEngagementFreezeStore, EfSchedulingStore>();
        services.AddScoped<FreezeEngagement>();
        services.AddAuthorization(options => options.AddPolicy("scheduling-closure", p => p.RequireAuthenticatedUser().RequireAssertion(c =>
            c.User.FindFirstValue("inklink_actor") == "service" && c.User.FindFirstValue("azp") == "inklink-engagements"
            && (c.User.FindFirstValue("scope") ?? "").Split(' ').Contains("scheduling.engagement.freeze", StringComparer.Ordinal))));
        return services;
    }
    public static IEndpointRouteBuilder MapSchedulingClosure(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("/api/v1/internal/engagements/{id:guid}/freeze", async (Guid id, FreezeInput input, FreezeEngagement command, CancellationToken ct) =>
        { await command.ExecuteAsync(id, input, ct); return Results.NoContent(); })
        .RequireAuthorization("scheduling-closure").Produces(204).ProducesValidationProblem().ProducesProblem(401).ProducesProblem(403).ProducesProblem(409).ProducesProblem(500);
        return endpoints;
    }
}
