using InkLink.Engagements.Application;
using InkLink.Engagements.Application.Negotiations;

namespace InkLink.Engagements.Api.Extensions;

internal static class NegotiationEndpointExtensions
{
    public static IEndpointRouteBuilder MapNegotiations(this IEndpointRouteBuilder endpoints)
    {
        endpoints.Map("/api/v1/engagements/{**remainder}", () => Results.Problem(statusCode: 410,
            title: "Engagements v1 was retired. Use /api/v2/engagements and its paginated proposals resource.",
            extensions: new Dictionary<string, object?> { ["code"] = "engagements.api_version_retired" }))
            .RequireAuthorization(InkLink.Authentication.AccessTokens.UserPolicy);
        var group = endpoints.MapGroup("/api/v2/engagements").WithTags("Engagements");
        group.MapPost("", async (RequestInput input, HttpContext http, ManageEngagements manage, CancellationToken cancellationToken) =>
        {
            var result = await manage.RequestAsync(input, Key(http), cancellationToken);
            return result.Created ? Results.Created($"/api/v2/engagements/{result.Value.Id}", result.Value) : Results.Ok(result.Value);
        }).RequireEngagementsAccount().Produces<EngagementView>(201).Produces<EngagementView>().WithErrors();
        group.MapGet("", (ManageEngagements manage, CancellationToken cancellationToken, string perspective = "client", int limit = 20, string? cursor = null) =>
            manage.ListAsync(perspective, limit, cursor, cancellationToken)).RequireEngagementsAccount().Produces<Page<EngagementView>>().WithErrors();
        group.MapGet("/{id:guid}", (Guid id, ManageEngagements manage, CancellationToken cancellationToken) => manage.ReadAsync(id, cancellationToken))
            .RequireEngagementsAccount().Produces<EngagementView>().WithErrors();
        group.MapPost("/{id:guid}/proposals", async (Guid id, ProposalInput input, HttpContext http, ManageEngagements manage, CancellationToken cancellationToken) =>
        {
            var result = await manage.PresentAsync(id, input, Key(http), cancellationToken);
            return result.Created ? Results.Created($"/api/v2/engagements/{id}/proposals/{result.Value.Id}", result.Value) : Results.Ok(result.Value);
        }).RequireEngagementsAccount().Produces<ProposalView>(201).Produces<ProposalView>().WithErrors();
        group.MapGet("/{id:guid}/proposals", (Guid id, ManageEngagements manage, CancellationToken cancellationToken, int limit = 20, string? cursor = null) =>
            manage.ListProposalsAsync(id, limit, cursor, cancellationToken)).RequireEngagementsAccount().Produces<Page<ProposalView>>().WithErrors();
        group.MapGet("/{id:guid}/proposals/{proposalId:guid}", (Guid id, Guid proposalId, ManageEngagements manage, CancellationToken cancellationToken) =>
            manage.ReadProposalAsync(id, proposalId, cancellationToken)).RequireEngagementsAccount().Produces<ProposalView>().WithErrors();
        group.MapPut("/{id:guid}/proposals/{proposalId:guid}/acceptance", (Guid id, Guid proposalId, ManageEngagements manage, CancellationToken cancellationToken) =>
            manage.AcceptAsync(id, proposalId, cancellationToken)).RequireEngagementsAccount().Produces<ProposalView>().WithErrors();
        return endpoints;
    }

    private static Guid Key(HttpContext context)
    {
        if (!Guid.TryParse(context.Request.Headers["Idempotency-Key"].ToString(), out var key) || key == Guid.Empty)
            throw EngagementsFailure.Invalid("idempotencyKey", "Idempotency-Key must be a nonempty UUID.");
        return key;
    }

    private static RouteHandlerBuilder WithErrors(this RouteHandlerBuilder endpoint) => endpoint.ProducesValidationProblem()
        .ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409).ProducesProblem(500).ProducesProblem(503);
}
