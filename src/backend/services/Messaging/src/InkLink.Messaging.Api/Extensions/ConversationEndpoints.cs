using System.Security.Claims;
using InkLink.Authentication;
using InkLink.Messaging.Application;
using InkLink.Messaging.Application.Conversations;

namespace InkLink.Messaging.Api.Extensions;

internal static class ConversationEndpoints
{
    private const string Technical = "messaging-block-writer";
    public static IServiceCollection AddMessagingTechnicalAuthorization(this IServiceCollection services) => services.AddAuthorization(options =>
        options.AddPolicy(Technical, policy => policy.RequireAuthenticatedUser().RequireAssertion(context =>
            context.User.FindFirstValue("inklink_actor") == "service" && context.User.FindFirstValue("azp") == "inklink-engagements"
            && (context.User.FindFirstValue("scope") ?? "").Split(' ').Contains("messaging.conversation.block", StringComparer.Ordinal))));

    public static IEndpointRouteBuilder MapConversations(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/conversations").WithTags("Conversations");
        group.MapGet("", (ManageConversations manage, CancellationToken ct, int limit = 20, string? cursor = null) => manage.ListAsync(limit, cursor, ct))
            .RequireMessagingAccount().Produces<Page<ConversationView>>().WithErrors();
        group.MapGet("/{id:guid}", (Guid id, ManageConversations manage, CancellationToken ct) => manage.ReadAsync(id, ct))
            .RequireMessagingAccount().Produces<ConversationView>().WithErrors();
        group.MapGet("/{id:guid}/messages", (Guid id, ManageConversations manage, CancellationToken ct, int limit = 20, string? cursor = null) => manage.HistoryAsync(id, limit, cursor, ct))
            .RequireMessagingAccount().Produces<Page<MessageView>>().WithErrors();
        group.MapPost("/{id:guid}/messages", async (Guid id, MessageInput input, HttpContext http, ManageConversations manage, CancellationToken ct) =>
        {
            if (!Guid.TryParse(http.Request.Headers["Idempotency-Key"].ToString(), out var key) || key == Guid.Empty)
                throw MessagingFailure.Invalid("idempotencyKey", "Nonempty UUID required.");
            var result = await manage.SendAsync(id, key, input, ct);
            return result.Created ? Results.Created($"/api/v1/conversations/{id}/messages/{result.Value.Id}", result.Value) : Results.Ok(result.Value);
        }).RequireMessagingAccount().Produces<MessageView>(201).Produces<MessageView>().WithErrors();
        group.MapGet("/{id:guid}/messages/{messageId:guid}", async (Guid id, Guid messageId, IConversationStore store, InkLink.Messaging.Application.Accounts.ICurrentMessagingAccount current, CancellationToken ct) =>
        {
            var account = current.Account ?? throw new MessagingFailure(MessagingError.Unauthenticated, "authentication.required");
            var message = await store.ReadMessageAsync(id, messageId, account.Id, ct);
            return message ?? throw ManageConversations.NotFound();
        }).RequireMessagingAccount().Produces<MessageView>().WithErrors();
        endpoints.MapPut("/api/v1/internal/engagements/{engagementId:guid}/writing-block", async (Guid engagementId, IConversationStore store, CancellationToken ct) =>
        {
            await store.BlockAsync(engagementId, ct); return Results.NoContent();
        }).RequireAuthorization(Technical).Produces(204).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(500);
        return endpoints;
    }

    private static RouteHandlerBuilder WithErrors(this RouteHandlerBuilder endpoint) => endpoint.ProducesValidationProblem()
        .ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409).ProducesProblem(500).ProducesProblem(503);
}
