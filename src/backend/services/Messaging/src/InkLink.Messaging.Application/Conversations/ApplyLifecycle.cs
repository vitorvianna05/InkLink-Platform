using InkLink.Messaging.Domain;
namespace InkLink.Messaging.Application.Conversations;

public sealed record LifecycleInput(Guid ClientId, Guid ArtistAccountId, bool Close);
public interface IConversationLifecycleStore
{
    Task ApplyAsync(Guid engagement, LifecycleInput input, Action<Conversation> change, CancellationToken ct);
}
public sealed class ApplyLifecycle(IConversationLifecycleStore store)
{
    public Task ExecuteAsync(Guid engagement, LifecycleInput input, CancellationToken ct)
    {
        if (engagement == Guid.Empty || input.ClientId == Guid.Empty || input.ArtistAccountId == Guid.Empty || input.ClientId == input.ArtistAccountId)
            throw new InvalidConversationData("participants", "Nonempty distinct participants required.");
        return store.ApplyAsync(engagement, input, conversation =>
        {
            conversation.VerifyParticipants(input.ClientId, input.ArtistAccountId);
            try { if (input.Close) conversation.Block(); else conversation.Activate(); }
            catch (InvalidOperationException ex) { throw new MessagingFailure(MessagingError.Conflict, ex.Message); }
        }, ct);
    }
}
