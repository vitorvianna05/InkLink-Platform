using InkLink.Messaging.Domain;

namespace InkLink.Messaging.Application.Conversations;

public sealed record MessageInput(string Body);
public sealed record MessageView(Guid Id, Guid ConversationId, Guid AuthorId, string Body, DateTimeOffset CreatedAt, long Sequence);
public sealed record ConversationRecord(Guid Id, Guid EngagementId, Guid ClientId, Guid ArtistAccountId, bool WritingBlocked, DateTimeOffset CreatedAt, MessageView? LastMessage);
public sealed record Page<T>(IReadOnlyList<T> Items, string? NextCursor, bool HasMore, int Total, string? ResumeCursor = null);
public sealed record ConversationOpened(Guid EngagementId, Guid ClientId, Guid ArtistAccountId, long Revision, bool WritingAllowed);
public sealed record EventEnvelope(Guid MessageId, string Type, int SchemaVersion, DateTimeOffset OccurredAt, string CorrelationId, string? CausationId, ConversationOpened Data,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? TraceParent = null);
public sealed record Creation<T>(T Value, bool Created);
public sealed record MessageChange<T>(T Result, Message? NewMessage = null);

public sealed record ConversationOpening(Guid MessageId, string Fingerprint, Conversation Conversation);
public sealed record OpeningState(string? ExistingFingerprint, Conversation? ExistingConversation);

public interface IConversationStore
{
    Task ApplyOpeningAsync(ConversationOpening opening, Action<OpeningState> validate, CancellationToken cancellationToken);
    Task<ConversationRecord?> ReadAsync(Guid id, Guid participant, CancellationToken cancellationToken);
    Task<Page<ConversationRecord>> ListAsync(Guid participant, int limit, Guid? after, CancellationToken cancellationToken);
    Task<Page<MessageView>> HistoryAsync(Guid id, Guid participant, int limit, long? after, CancellationToken cancellationToken);
    Task<MessageView?> ReadMessageAsync(Guid id, Guid messageId, Guid participant, CancellationToken cancellationToken);
    Task<T> ChangeAsync<T>(Guid id, Guid participant, Guid key, Func<Conversation, Message?, MessageChange<T>> change, CancellationToken cancellationToken);
    Task BlockAsync(Guid engagement, CancellationToken cancellationToken);
}


public sealed record Counterpart(Guid Id, string DisplayName);
public sealed record ConversationPresentation(Counterpart Counterpart, string EngagementState);
public sealed record ConversationView(Guid Id, Guid EngagementId, Guid ClientId, Guid ArtistAccountId, bool WritingBlocked, DateTimeOffset CreatedAt,
    MessageView? LastMessage, Counterpart Counterpart, string EngagementState);
public interface IConversationPresentationDirectory
{
    Task<ConversationPresentation> ReadAsync(ConversationRecord conversation, Guid participant, CancellationToken cancellationToken);
}
