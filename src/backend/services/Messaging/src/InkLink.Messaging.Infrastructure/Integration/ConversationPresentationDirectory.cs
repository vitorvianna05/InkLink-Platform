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
                || context.ArtistAccountId != conversation.ArtistAccountId || context.State is not ("Negotiating" or "Agreed" or "Closing" or "Closed")
                || string.IsNullOrWhiteSpace(context.Catalog?.Name) || (participant != context.ClientId && participant != context.ArtistAccountId)) throw Unavailable();
            Counterpart counterpart;
            if (participant == context.ClientId)
            {
                // A foto pública do artista é opcional: indisponibilidade da Identity não impede a conversa.
                ParticipantProfile? artist = null;
                try { artist = await profiles.ProfileAsync(context.ArtistAccountId, cancellationToken); }
                catch (MessagingFailure) { }
                counterpart = new Counterpart(context.ArtistAccountId, context.Catalog.Name, artist?.PhotoUrl, artist?.PhotoThumbnailUrl);
            }
            else
            {
                var client = await profiles.ProfileAsync(context.ClientId, cancellationToken);
                counterpart = new Counterpart(context.ClientId, client.DisplayName, client.PhotoUrl, client.PhotoThumbnailUrl);
            }
            return new(counterpart, context.State);
        }
        catch (Exception exception) when (exception is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        { throw Unavailable(); }
    }

    public static MessagingFailure Unavailable() => new(MessagingError.Unavailable, "messaging.presentation_unavailable");
    private sealed record ArtistSnapshot(string Name);
    private sealed record EngagementContext(Guid Id, Guid ClientId, Guid ArtistAccountId, string State, ArtistSnapshot Catalog);
}
