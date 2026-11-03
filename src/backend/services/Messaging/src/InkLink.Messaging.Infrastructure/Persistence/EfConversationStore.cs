using System.Data;
using InkLink.Messaging.Application;
using InkLink.Messaging.Application.Conversations;
using InkLink.Messaging.Domain;
using Microsoft.EntityFrameworkCore;

namespace InkLink.Messaging.Infrastructure.Persistence;

public sealed class EfConversationStore(MessagingDbContext database) : IConversationStore
{
    public async Task ApplyOpeningAsync(ConversationOpening opening, Action<OpeningState> validate, CancellationToken ct)
    {
        var opened = opening.Conversation;
        await using var transaction = await database.Database.BeginTransactionAsync(ct);
        var inserted = await database.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO messaging.inbox (\"Id\", \"Fingerprint\", \"ProcessedAt\") VALUES ({opening.MessageId}, {opening.Fingerprint}, {DateTimeOffset.UtcNow}) ON CONFLICT DO NOTHING", ct);
        if (inserted == 0)
        {
            var existing = await database.Inbox.AsNoTracking().SingleAsync(x => x.Id == opening.MessageId, ct);
            validate(new(existing.Fingerprint, null));
            await transaction.CommitAsync(ct); return;
        }
        await database.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO messaging.conversations (\"Id\", \"EngagementId\", \"ClientId\", \"ArtistAccountId\", \"CreatedAt\", \"WritingBlocked\", \"Revision\", \"LastMessageSequence\") VALUES ({opened.Id}, {opened.EngagementId}, {opened.ClientId}, {opened.ArtistAccountId}, {opened.CreatedAt}, {opened.WritingBlocked}, {opened.Revision}, {opened.LastMessageSequence}) ON CONFLICT (\"EngagementId\") DO NOTHING", ct);
        var conversation = await database.Conversations.AsNoTracking().SingleAsync(c => c.EngagementId == opened.EngagementId, ct);
        validate(new(null, conversation));
        await transaction.CommitAsync(ct);
    }

    public async Task<ConversationRecord?> ReadAsync(Guid id, Guid participant, CancellationToken ct)
    {
        var conversation = await database.Conversations.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id && (c.ClientId == participant || c.ArtistAccountId == participant), ct);
        if (conversation is null) return null;
        var last = await database.Messages.AsNoTracking().Where(m => m.ConversationId == id).OrderByDescending(m => m.Sequence).FirstOrDefaultAsync(ct);
        return View(conversation, last);
    }

    public async Task<Page<ConversationRecord>> ListAsync(Guid participant, int limit, Guid? after, CancellationToken ct)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var query = database.Conversations.AsNoTracking().Where(c => c.ClientId == participant || c.ArtistAccountId == participant);
        var total = await query.CountAsync(ct);
        if (after.HasValue) query = query.Where(c => c.Id.CompareTo(after.Value) > 0);
        var rows = await query.OrderBy(c => c.Id).Take(limit + 1)
            .Select(conversation => new
            {
                Conversation = conversation,
                Last = database.Messages.AsNoTracking()
                .Where(message => message.ConversationId == conversation.Id).OrderByDescending(message => message.Sequence).FirstOrDefault()
            })
            .ToListAsync(ct);
        return new(rows.Take(limit).Select(row => View(row.Conversation, row.Last)).ToArray(), null, rows.Count > limit, total);
    }

    public async Task<Page<MessageView>> HistoryAsync(Guid id, Guid participant, int limit, long? after, CancellationToken ct)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        if (!await database.Conversations.AnyAsync(c => c.Id == id && (c.ClientId == participant || c.ArtistAccountId == participant), ct)) throw ManageConversations.NotFound();
        var query = database.Messages.AsNoTracking().Where(m => m.ConversationId == id);
        var total = await query.CountAsync(ct);
        if (after.HasValue) query = query.Where(m => m.Sequence > after.Value);
        var items = await query.OrderBy(m => m.Sequence).Take(limit + 1).ToListAsync(ct);
        return new(items.Take(limit).Select(View).ToArray(), null, items.Count > limit, total);
    }

    public async Task<T> ChangeAsync<T>(Guid id, Guid participant, Guid key, Func<Conversation, Message?, MessageChange<T>> change, CancellationToken ct)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(ct);
        var conversation = await database.Conversations.FromSqlInterpolated($"SELECT * FROM messaging.conversations WHERE \"Id\" = {id} AND (\"ClientId\" = {participant} OR \"ArtistAccountId\" = {participant}) FOR UPDATE").SingleOrDefaultAsync(ct) ?? throw ManageConversations.NotFound();
        var previous = await database.Messages.SingleOrDefaultAsync(m => m.ConversationId == id && m.AuthorId == participant && m.RequestKey == key, ct);
        var mutation = change(conversation, previous);
        if (mutation.NewMessage is not null) database.Messages.Add(mutation.NewMessage);
        await database.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return mutation.Result;
    }

    public async Task<MessageView?> ReadMessageAsync(Guid id, Guid messageId, Guid participant, CancellationToken ct)
    {
        var message = await database.Messages.AsNoTracking().Where(m => m.Id == messageId && m.ConversationId == id
            && database.Conversations.Any(c => c.Id == id && (c.ClientId == participant || c.ArtistAccountId == participant))).SingleOrDefaultAsync(ct);
        return message is null ? null : View(message);
    }

    public async Task BlockAsync(Guid engagement, CancellationToken ct)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(ct);
        var conversation = await database.Conversations.FromSqlInterpolated($"SELECT * FROM messaging.conversations WHERE \"EngagementId\" = {engagement} FOR UPDATE").SingleOrDefaultAsync(ct) ?? throw ManageConversations.NotFound();
        conversation.Block(); await database.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
    }
    private static ConversationRecord View(Conversation c, Message? last) => new(c.Id, c.EngagementId, c.ClientId, c.ArtistAccountId, c.WritingBlocked, c.CreatedAt, last is null ? null : View(last));
    private static MessageView View(Message m) => new(m.Id, m.ConversationId, m.AuthorId, m.Body, m.CreatedAt, m.Sequence);
}
