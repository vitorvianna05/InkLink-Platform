using InkLink.Messaging.Application.Conversations;
using Microsoft.EntityFrameworkCore;
namespace InkLink.Messaging.Infrastructure.Persistence;

public sealed class EfArtistConversationSummaryStore(MessagingDbContext db) : IArtistConversationSummaryStore
{
    public Task<int> CountAsync(Guid artist, CancellationToken ct) =>
        db.Conversations.AsNoTracking().CountAsync(x => x.ArtistAccountId == artist && !x.WritingBlocked, ct);
}
