namespace InkLink.Messaging.Domain;

public sealed class InvalidConversationData(string field, string message) : Exception(message)
{
    public string Field { get; } = field;
}

public sealed class Conversation
{
    private Conversation() { }
    public Guid Id { get; private set; }
    public Guid EngagementId { get; private set; }
    public Guid ClientId { get; private set; }
    public Guid ArtistAccountId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public bool WritingBlocked { get; private set; }
    public bool Closed { get; private set; }
    public long Revision { get; private set; }
    public long LastMessageSequence { get; private set; }

    public static Conversation Open(Guid engagement, Guid client, Guid artist, DateTimeOffset createdAt, long revision)
    {
        if (engagement == Guid.Empty || client == Guid.Empty || artist == Guid.Empty || client == artist || revision <= 0)
            throw new InvalidConversationData("participants", "Invalid engagement or participants.");
        return new Conversation
        {
            Id = Guid.CreateVersion7(),
            EngagementId = engagement,
            ClientId = client,
            ArtistAccountId = artist,
            CreatedAt = createdAt.ToUniversalTime(),
            Revision = revision
        };
    }

    public void VerifyParticipants(Guid client, Guid artist)
    {
        if (client != ClientId || artist != ArtistAccountId) throw new InvalidConversationData("participants", "Participants cannot change.");
    }

    public void Block() { WritingBlocked = true; Closed = true; }
    public void AwaitReservation() => WritingBlocked = true;
    public void Activate()
    {
        if (Closed) throw new InvalidOperationException("messaging.closed");
        WritingBlocked = false;
    }
    public void EnsureWritable() { if (WritingBlocked) throw new InvalidOperationException("messaging.writing_blocked"); }
    public long AllocateMessageSequence()
    {
        EnsureWritable();
        LastMessageSequence = checked(LastMessageSequence + 1);
        return LastMessageSequence;
    }
}

public sealed class Message
{
    private Message() { }
    public Guid Id { get; private set; }
    public Guid ConversationId { get; private set; }
    public Guid AuthorId { get; private set; }
    public Guid RequestKey { get; private set; }
    public long Sequence { get; private set; }
    public string Body { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    public static Message Create(Guid conversation, Guid author, Guid key, string? body, DateTimeOffset now, long sequence = 1)
    {
        var text = body?.Trim() ?? string.Empty;
        if (conversation == Guid.Empty || author == Guid.Empty || key == Guid.Empty || sequence <= 0) throw new InvalidConversationData("id", "Nonempty UUID and positive sequence required.");
        if (text.Length == 0 || (body?.Length ?? 0) > 4000) throw new InvalidConversationData("body", "Message must contain 1–4000 characters.");
        var utc = now.ToUniversalTime();
        return new Message
        {
            Id = Guid.CreateVersion7(),
            ConversationId = conversation,
            AuthorId = author,
            RequestKey = key,
            Sequence = sequence,
            Body = text,
            CreatedAt = new DateTimeOffset(utc.Ticks - utc.Ticks % 10, TimeSpan.Zero)
        };
    }
}
