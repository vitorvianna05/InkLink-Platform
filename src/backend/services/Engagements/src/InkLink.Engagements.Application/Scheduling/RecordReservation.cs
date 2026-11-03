using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using InkLink.Engagements.Domain;

namespace InkLink.Engagements.Application.Scheduling;

public sealed record ReservationNotice(Guid ReservationId, Guid EngagementId, Guid ClientId, Guid ArtistAccountId, Guid OperationId,
    DateTimeOffset Start, DateTimeOffset End, string TimeZoneId, long Revision);
public sealed record ReservationEnvelope(Guid MessageId, string Type, int SchemaVersion, DateTimeOffset OccurredAt, string CorrelationId,
    Guid? CausationId, ReservationNotice Data, string? TraceParent = null);
public interface IReservationReceiptStore
{
    Task ApplyAsync(ReservationEnvelope envelope, string fingerprint, Action<Engagement, ReservationNotice?, string?> validate, CancellationToken ct);
}
public sealed class RecordReservation(IReservationReceiptStore store)
{
    public Task ExecuteAsync(ReservationEnvelope envelope, CancellationToken ct)
    {
        var data = envelope.Data;
        if (envelope.MessageId == Guid.Empty || envelope.Type != "scheduling.reservation-confirmed.v1" || envelope.SchemaVersion != 1
            || envelope.OccurredAt == default || string.IsNullOrWhiteSpace(envelope.CorrelationId) || data is null
            || data.ReservationId == Guid.Empty || data.EngagementId == Guid.Empty || data.ClientId == Guid.Empty || data.ArtistAccountId == Guid.Empty
            || data.ClientId == data.ArtistAccountId || data.OperationId == Guid.Empty || data.End <= data.Start || data.Revision != 1
            || string.IsNullOrWhiteSpace(data.TimeZoneId))
            throw new InvalidEngagementData("event", "Unsupported reservation event.");
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope))));
        return store.ApplyAsync(envelope, fingerprint, (engagement, previous, existingFingerprint) =>
        {
            if (engagement.ClientId != data.ClientId || engagement.ArtistAccountId != data.ArtistAccountId
                || (previous is not null && previous != data) || (existingFingerprint is not null && existingFingerprint != fingerprint))
                throw new InvalidEngagementData("event", "Incompatible reservation context.");
        }, ct);
    }
}
