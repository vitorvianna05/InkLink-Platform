using InkLink.Messaging.Application.Conversations;
using InkLink.Messaging.Domain;
using Microsoft.EntityFrameworkCore;
namespace InkLink.Messaging.Infrastructure.Persistence;

public sealed class EfConversationLifecycleStore(MessagingDbContext db) : IConversationLifecycleStore
{
    public async Task ApplyAsync(Guid engagement, LifecycleInput input, Action<Conversation> change, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var opened = Conversation.Open(engagement, input.ClientId, input.ArtistAccountId, DateTimeOffset.UtcNow, 1);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO messaging.conversations (\"Id\",\"EngagementId\",\"ClientId\",\"ArtistAccountId\",\"CreatedAt\",\"WritingBlocked\",\"Closed\",\"Revision\",\"LastMessageSequence\") VALUES ({opened.Id},{engagement},{input.ClientId},{input.ArtistAccountId},{opened.CreatedAt},true,false,1,0) ON CONFLICT (\"EngagementId\") DO NOTHING", ct);
        var conversation = await db.Conversations.FromSqlInterpolated($"SELECT * FROM messaging.conversations WHERE \"EngagementId\"={engagement} FOR UPDATE").SingleAsync(ct);
        change(conversation);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
}
