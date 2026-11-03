using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using InkLink.Messaging.Domain;

namespace InkLink.Messaging.Application.Conversations;

public sealed class OpenConversation(IConversationStore store)
{
    public Task ExecuteAsync(EventEnvelope envelope, CancellationToken ct)
    {
        if (envelope.MessageId == Guid.Empty || envelope.Type != "engagement.negotiation-opened.v1" || envelope.SchemaVersion != 1
            || envelope.OccurredAt == default || string.IsNullOrWhiteSpace(envelope.CorrelationId) || envelope.Data is null
           ) throw new InvalidConversationData("event", "Unsupported event.");
        var opened = Conversation.Open(envelope.Data.EngagementId, envelope.Data.ClientId, envelope.Data.ArtistAccountId, envelope.OccurredAt, envelope.Data.Revision);
        if (!envelope.Data.WritingAllowed) opened.AwaitReservation();
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope))));
        return store.ApplyOpeningAsync(new(envelope.MessageId, fingerprint, opened), state =>
        {
            if (state.ExistingFingerprint is not null && state.ExistingFingerprint != fingerprint)
                throw new InvalidConversationData("messageId", "Repeated event ID has different content.");
            state.ExistingConversation?.VerifyParticipants(opened.ClientId, opened.ArtistAccountId);
        }, ct);
    }
}
