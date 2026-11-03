using InkLink.Messaging.Application.Conversations;
namespace InkLink.Messaging.Api.Extensions;

internal static class ArtistSummaryExtensions
{
    public static IServiceCollection AddArtistConversationSummary(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System); services.AddScoped<ReadArtistConversationSummary>(); return services;
    }
    public static IEndpointRouteBuilder MapArtistConversationSummary(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/conversations/artist-summary", (ReadArtistConversationSummary read, CancellationToken ct) => read.ExecuteAsync(ct))
            .RequireMessagingAccount().WithTags("Conversations").Produces<ArtistConversationSummary>()
            .ProducesProblem(401).ProducesProblem(403).ProducesProblem(500).ProducesProblem(503);
        return endpoints;
    }
}
