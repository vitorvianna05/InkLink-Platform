using InkLink.Messaging.Domain;

namespace InkLink.Messaging.UnitTests;

public sealed class ConversationTests
{
    [Fact]
    public void Blocking_is_idempotent_and_rejects_future_writes()
    {
        var c = Conversation.Open(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, 1);
        c.EnsureWritable(); c.Block(); c.Block();
        Assert.True(c.WritingBlocked);
        Assert.Throws<InvalidOperationException>(c.EnsureWritable);
    }

    [Fact]
    public void Participant_replacement_is_rejected()
    {
        var client = Guid.NewGuid(); var artist = Guid.NewGuid();
        var c = Conversation.Open(Guid.NewGuid(), client, artist, DateTimeOffset.UtcNow, 1);
        c.VerifyParticipants(client, artist);
        Assert.Throws<InvalidConversationData>(() => c.VerifyParticipants(Guid.NewGuid(), artist));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_message_is_rejected(string body) => Assert.Throws<InvalidConversationData>(() =>
        Message.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), body, DateTimeOffset.UtcNow));
}
