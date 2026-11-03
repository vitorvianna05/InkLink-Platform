namespace InkLink.Messaging.Application.Conversations;

public sealed class BlockConversation(IConversationStore store)
{
    public Task ExecuteAsync(Guid engagementId, CancellationToken cancellationToken) =>
        store.ChangeWritingAsync(engagementId, conversation => conversation.Block(), cancellationToken);
}
