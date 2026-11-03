using System.Diagnostics;
using System.Text.Json;
using InkLink.Scheduling.Domain;

namespace InkLink.Scheduling.Infrastructure.Persistence;

public sealed class OutboxEvent
{
    public Guid Id { get; set; }
    public Guid ReservationId { get; set; }
    public string Payload { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public DateTimeOffset? QuarantinedAt { get; set; }
    public void RecordFailure(string error, DateTimeOffset now)
    {
        Attempts++; LastError = error.Length <= 200 ? error : error[..200];
        if (Attempts >= 10) { QuarantinedAt = now; NextAttemptAt = null; }
        else NextAttemptAt = now.AddSeconds(Math.Min(300, 2 * Math.Pow(2, Attempts - 1)));
    }
    public static OutboxEvent ForReservation(Reservation r)
    {
        var id = Guid.CreateVersion7();
        return new OutboxEvent
        {
            Id = id,
            ReservationId = r.Id,
            CreatedAt = r.ConfirmedAt,
            Payload = JsonSerializer.Serialize(new
            {
                messageId = id,
                type = "scheduling.reservation-confirmed.v1",
                schemaVersion = 1,
                occurredAt = r.ConfirmedAt,
                correlationId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N"),
                traceParent = Activity.Current?.Id,
                causationId = r.OperationId,
                data = new
                {
                    reservationId = r.Id,
                    engagementId = r.EngagementId,
                    clientId = r.ClientId,
                    artistAccountId = r.ArtistId,
                    operationId = r.OperationId,
                    start = r.Start,
                    end = r.End,
                    timeZoneId = r.TimeZoneId,
                    revision = 1
                }
            })
        };
    }
}
