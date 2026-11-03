using System.Text;
using InkLink.Messaging.Application.Accounts;
using InkLink.Messaging.Domain;

namespace InkLink.Messaging.Application.Conversations;

public sealed class ManageConversations(ICurrentMessagingAccount current, IConversationStore store, IConversationPresentationDirectory presentation)
{
    public Task<ConversationView> ReadAsync(Guid id, CancellationToken ct) => ReadCoreAsync(id, AccountAccess.Require(current).Id, ct);
    private async Task<ConversationView> ReadCoreAsync(Guid id, Guid participant, CancellationToken ct)
    {
        var conversation = await store.ReadAsync(id, participant, ct) ?? throw NotFound();
        return await PresentAsync(conversation, participant, ct);
    }

    private async Task<ConversationView> PresentAsync(ConversationRecord conversation, Guid participant, CancellationToken ct)
    {
        var context = await presentation.ReadAsync(conversation, participant, ct);
        var counterpart = participant == conversation.ClientId ? conversation.ArtistAccountId : conversation.ClientId;
        if (context.Counterpart.Id != counterpart || string.IsNullOrWhiteSpace(context.Counterpart.DisplayName) || string.IsNullOrWhiteSpace(context.EngagementState))
            throw new MessagingFailure(MessagingError.Unavailable, "messaging.presentation_unavailable");
        return new(conversation.Id, conversation.EngagementId, conversation.ClientId, conversation.ArtistAccountId,
            conversation.WritingBlocked, conversation.CreatedAt, conversation.LastMessage, context.Counterpart, context.EngagementState);
    }
    public async Task<Page<ConversationView>> ListAsync(int limit, string? cursor, CancellationToken ct)
    {
        var account = AccountAccess.Require(current);
        var prefix = $"v1|conversations|{account.Id}|";
        var page = await store.ListAsync(account.Id, Limit(limit), Parse(cursor, prefix), ct);
        var items = new ConversationView[page.Items.Count];
        await Parallel.ForEachAsync(Enumerable.Range(0, items.Length), new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct },
            async (index, token) => items[index] = await PresentAsync(page.Items[index], account.Id, token));
        return new(items, page.HasMore ? Cursor(prefix, page.Items[^1].Id) : null, page.HasMore, page.Total);
    }
    public async Task<Page<MessageView>> HistoryAsync(Guid id, int limit, string? cursor, CancellationToken ct)
    {
        var account = AccountAccess.Require(current);
        var prefix = $"v2|messages|{account.Id}|{id}|";
        var page = await store.HistoryAsync(id, account.Id, Limit(limit), ParseMessage(cursor, prefix), ct);
        var resume = page.Items.Count > 0 ? Convert.ToBase64String(Encoding.UTF8.GetBytes(prefix + page.Items[^1].Sequence)) : cursor;
        return page with { NextCursor = page.HasMore ? resume : null, ResumeCursor = resume };
    }
    public Task<Creation<MessageView>> SendAsync(Guid id, Guid key, MessageInput input, CancellationToken ct)
    {
        var account = AccountAccess.Require(current);
        var validated = Message.Create(id, account.Id, key, input.Body, DateTimeOffset.UtcNow);
        return store.ChangeAsync(id, account.Id, key, (conversation, previous) =>
        {
            if (previous is not null)
            {
                if (previous.Body != validated.Body) throw new MessagingFailure(MessagingError.Conflict, "messaging.idempotency_conflict");
                return new MessageChange<Creation<MessageView>>(new(View(previous), false));
            }
            long sequence;
            try { sequence = conversation.AllocateMessageSequence(); }
            catch (InvalidOperationException e) { throw new MessagingFailure(MessagingError.Conflict, e.Message); }
            var message = Message.Create(id, account.Id, key, validated.Body, DateTimeOffset.UtcNow, sequence);
            return new MessageChange<Creation<MessageView>>(new(View(message), true), message);
        }, ct);
    }
    public async Task<MessageView> ReadMessageAsync(Guid id, Guid messageId, CancellationToken ct)
    {
        var account = AccountAccess.Require(current);
        return await store.ReadMessageAsync(id, messageId, account.Id, ct) ?? throw NotFound();
    }

    public static MessagingFailure NotFound() => new(MessagingError.NotFound, "messaging.not_found");
    private static int Limit(int value) => value is >= 1 and <= 100 ? value : throw MessagingFailure.Invalid("limit", "Use 1 to 100.");
    private static string Cursor(string prefix, Guid id) => Convert.ToBase64String(Encoding.UTF8.GetBytes(prefix + id));
    private static Guid? Parse(string? cursor, string prefix)
    {
        if (cursor is null) return null;
        try
        {
            if (cursor.Length > 500) throw new FormatException();
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            if (!decoded.StartsWith(prefix, StringComparison.Ordinal) || !Guid.TryParse(decoded[prefix.Length..], out var result) || result == Guid.Empty)
                throw new FormatException();
            return result;
        }
        catch (FormatException) { throw MessagingFailure.Invalid("cursor", "Cursor does not match this query."); }
    }
    private static MessageView View(Message m) => new(m.Id, m.ConversationId, m.AuthorId, m.Body, m.CreatedAt, m.Sequence);
    private static long? ParseMessage(string? cursor, string prefix)
    {
        if (cursor is null) return null;
        try
        {
            if (cursor.Length > 500) throw new FormatException();
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            if (!decoded.StartsWith(prefix, StringComparison.Ordinal)) throw new FormatException();
            if (!long.TryParse(decoded[prefix.Length..], out var sequence) || sequence <= 0) throw new FormatException();
            return sequence;
        }
        catch (FormatException) { throw MessagingFailure.Invalid("cursor", "Cursor does not match this query."); }
    }
}
