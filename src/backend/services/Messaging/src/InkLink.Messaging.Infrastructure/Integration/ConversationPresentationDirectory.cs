using System.Net.Http.Json;
using InkLink.Messaging.Application;
using InkLink.Messaging.Application.Conversations;

namespace InkLink.Messaging.Infrastructure.Integration;

public sealed class ConversationPresentationDirectory(HttpClient engagements, ParticipantProfiles profiles) : IConversationPresentationDirectory
{
    public async Task<ConversationPresentation> ReadAsync(ConversationRecord conversation, Guid participant, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await engagements.GetAsync($"api/v2/engagements/{conversation.EngagementId}", cancellationToken);
            if (!response.IsSuccessStatusCode) throw Unavailable();
            var context = await response.Content.ReadFromJsonAsync<EngagementContext>(cancellationToken);
            if (context is null || context.Id != conversation.EngagementId || context.ClientId != conversation.ClientId
                || context.ArtistAccountId != conversation.ArtistAccountId || context.State is not ("Negotiating" or "Agreed")
                || string.IsNullOrWhiteSpace(context.Catalog?.Name) || (participant != context.ClientId && participant != context.ArtistAccountId)) throw Unavailable();
            var counterpart = participant == context.ClientId
                ? new Counterpart(context.ArtistAccountId, context.Catalog.Name)
                : new Counterpart(context.ClientId, await profiles.NameAsync(context.ClientId, cancellationToken));
            return new(counterpart, context.State);
        }
        catch (Exception exception) when (exception is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        { throw Unavailable(); }
    }

    public static MessagingFailure Unavailable() => new(MessagingError.Unavailable, "messaging.presentation_unavailable");
    private sealed record ArtistSnapshot(string Name);
    private sealed record EngagementContext(Guid Id, Guid ClientId, Guid ArtistAccountId, string State, ArtistSnapshot Catalog);
}
