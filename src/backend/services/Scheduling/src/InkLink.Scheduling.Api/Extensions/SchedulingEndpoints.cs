using InkLink.Scheduling.Application;
using InkLink.Scheduling.Application.Reservations;

namespace InkLink.Scheduling.Api.Extensions;

internal static class SchedulingEndpoints
{
    public static IEndpointRouteBuilder MapScheduling(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/scheduling").WithTags("Scheduling");
        group.MapPut("/availability/{date}", (DateOnly date, AvailabilityInput input, ManageSchedule manage, CancellationToken ct) => manage.DefineAsync(date, input, ct))
            .RequireSchedulingAccount().Produces<AvailabilityView>().Errors();
        group.MapGet("/availability/{date}", (DateOnly date, ManageSchedule manage, CancellationToken ct) => manage.OwnAvailabilityAsync(date, ct))
            .RequireSchedulingAccount().Produces<AvailabilityView>().Errors();
        group.MapGet("/engagements/{engagementId:guid}/availability/{date}", (Guid engagementId, DateOnly date, ManageSchedule manage, CancellationToken ct) => manage.AvailabilityAsync(engagementId, date, ct))
            .RequireSchedulingAccount().Produces<AvailabilityView>().Errors();
        group.MapPost("/reservations", async (ReserveInput input, HttpContext http, ManageSchedule manage, CancellationToken ct) =>
        {
            if (!Guid.TryParse(http.Request.Headers["Idempotency-Key"], out var key) || key == Guid.Empty) throw SchedulingFailure.Invalid("idempotencyKey", "Nonempty UUID required.");
            var result = await manage.ConfirmAsync(key, input, ct);
            return result.Created ? Results.Created($"/api/v1/scheduling/reservations/{result.Value.Id}", result.Value) : Results.Ok(result.Value);
        }).RequireSchedulingAccount().Produces<ReservationView>(201).Produces<ReservationView>().Errors();
        group.MapGet("/reservations/{id:guid}", (Guid id, ManageSchedule manage, CancellationToken ct) => manage.ReadAsync(id, ct))
            .RequireSchedulingAccount().Produces<ReservationView>().Errors();
        group.MapGet("/operations/{operationId:guid}", (Guid operationId, ManageSchedule manage, CancellationToken ct) => manage.OperationAsync(operationId, ct))
            .RequireSchedulingAccount().Produces<ReservationView>().Errors();
        group.MapGet("/calendar", (DateOnly from, DateOnly to, ManageSchedule manage, CancellationToken ct, string perspective = "artist", int limit = 20, string? cursor = null) =>
            manage.ListAsync(perspective, from, to, limit, cursor, ct)).RequireSchedulingAccount().Produces<Page<ReservationView>>().Errors();
        return endpoints;
    }
    private static RouteHandlerBuilder Errors(this RouteHandlerBuilder route) => route.ProducesValidationProblem()
        .ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409).ProducesProblem(500).ProducesProblem(503);
}
