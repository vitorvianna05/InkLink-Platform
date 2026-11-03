using InkLink.Messaging.Application.Accounts;
namespace InkLink.Messaging.Application.Conversations;

public sealed record ArtistConversationSummary(Guid ArtistId, int ActiveConversations, DateTimeOffset ObservedAt);
public interface IArtistConversationSummaryStore { Task<int> CountAsync(Guid artist, CancellationToken ct); }
public sealed class ReadArtistConversationSummary(ICurrentMessagingAccount current, IArtistConversationSummaryStore store, TimeProvider clock)
{
    public async Task<ArtistConversationSummary> ExecuteAsync(CancellationToken ct)
    {
        var account = current.Account ?? throw new MessagingFailure(MessagingError.Unauthenticated, "authentication.required");
        if (!account.IsArtist) throw new MessagingFailure(MessagingError.Forbidden, "messaging.artist_required");
        return new(account.Id, await store.CountAsync(account.Id, ct), clock.GetUtcNow());
    }
}
