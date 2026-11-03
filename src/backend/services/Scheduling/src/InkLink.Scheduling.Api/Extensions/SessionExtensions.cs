using InkLink.Scheduling.Application.Reservations;
namespace InkLink.Scheduling.Api.Extensions;

internal static class SessionExtensions
{
    public static IServiceCollection AddSchedulingSessions(this IServiceCollection services)
    { services.AddScoped<ManageSessions>(); return services; }
    public static IEndpointRouteBuilder MapSchedulingSessions(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/scheduling").WithTags("Sessions and management");
        group.MapPut("/reservations/{id:guid}/completion", (Guid id, CompletionInput input, ManageSessions manage, CancellationToken ct) => manage.CompleteAsync(id, input, ct))
            .RequireSchedulingAccount().Produces<CompletionView>().WithSessionErrors();
        group.MapGet("/reservations/{id:guid}/completion", (Guid id, ManageSessions manage, CancellationToken ct) => manage.ReadAsync(id, ct))
            .RequireSchedulingAccount().Produces<CompletionView>().WithSessionErrors();
        group.MapGet("/artist-summary", (DateOnly from, DateOnly to, ManageSessions manage, CancellationToken ct) => manage.SummaryAsync(from, to, ct))
            .RequireSchedulingAccount().Produces<ArtistScheduleSummary>().WithSessionErrors();
        group.MapGet("/artist-context", (ManageSessions manage, CancellationToken ct) => manage.CalendarAsync(ct))
            .RequireSchedulingAccount().Produces<ArtistCalendarContext>().WithSessionErrors();
        return endpoints;
    }
    private static RouteHandlerBuilder WithSessionErrors(this RouteHandlerBuilder endpoint) => endpoint.ProducesValidationProblem()
        .ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409).ProducesProblem(500).ProducesProblem(503);
}
