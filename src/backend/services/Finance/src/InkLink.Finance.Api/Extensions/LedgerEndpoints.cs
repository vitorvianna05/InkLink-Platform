using InkLink.Finance.Application;
using InkLink.Finance.Application.Ledger;
namespace InkLink.Finance.Api.Extensions;

internal static class LedgerEndpoints
{
    public static IEndpointRouteBuilder MapFinanceLedger(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/finance").WithTags("Finance");
        group.MapPost("/entries", async (EntryInput input, HttpContext http, ManageLedger manage, CancellationToken ct) =>
        {
            var result = await manage.RecordAsync(input, Key(http), ct);
            return result.Created ? Results.Created($"/api/v1/finance/entries/{result.Value.Id}", result.Value) : Results.Ok(result.Value);
        }).RequireFinanceAccount().Produces<EntryView>(201).Produces<EntryView>().WithErrors();
        group.MapGet("/entries/{id:guid}", (Guid id, ManageLedger manage, CancellationToken ct) => manage.ReadAsync(id, ct))
            .RequireFinanceAccount().Produces<EntryView>().WithErrors();
        group.MapGet("/entries", (DateOnly from, DateOnly to, ManageLedger manage, CancellationToken ct, int limit = 20, string? cursor = null, string? kind = null) =>
            manage.ListAsync(from, to, kind, limit, cursor, ct)).RequireFinanceAccount().Produces<Page<EntryView>>().WithErrors();
        group.MapPost("/entries/{id:guid}/reversal", async (Guid id, ReversalInput input, HttpContext http, ManageLedger manage, CancellationToken ct) =>
        {
            var result = await manage.ReverseAsync(id, input, Key(http), ct);
            return result.Created ? Results.Created($"/api/v1/finance/entries/{result.Value.Id}", result.Value) : Results.Ok(result.Value);
        }).RequireFinanceAccount().Produces<EntryView>(201).Produces<EntryView>().WithErrors();
        group.MapGet("/dashboard", (DateOnly from, DateOnly to, ManageLedger manage, CancellationToken ct) => manage.DashboardAsync(from, to, ct))
            .RequireFinanceAccount().Produces<Dashboard>().WithErrors();
        return endpoints;
    }
    private static Guid Key(HttpContext http) => Guid.TryParse(http.Request.Headers["Idempotency-Key"], out var value) && value != Guid.Empty
        ? value : throw FinanceFailure.Invalid("idempotencyKey", "Nonempty Idempotency-Key UUID required.");
    private static RouteHandlerBuilder WithErrors(this RouteHandlerBuilder endpoint) => endpoint.ProducesValidationProblem()
        .ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409).ProducesProblem(500).ProducesProblem(503);
}
