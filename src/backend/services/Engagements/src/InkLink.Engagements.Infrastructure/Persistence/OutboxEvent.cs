using System.Diagnostics;
using System.Text.Json;
using InkLink.Engagements.Domain;

namespace InkLink.Engagements.Infrastructure.Persistence;

public sealed class OutboxEvent
{
    public Guid Id { get; set; }
    public Guid EngagementId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public DateTimeOffset? QuarantinedAt { get; set; }

    public void RecordFailure(string error, DateTimeOffset now)
    {
        Attempts++;
        LastError = error.Length <= 200 ? error : error[..200];
        if (Attempts >= 10) { QuarantinedAt = now; NextAttemptAt = null; return; }
        NextAttemptAt = now.AddSeconds(Math.Min(300, 2 * Math.Pow(2, Attempts - 1)));
    }

    public void ResetForRetry() { Attempts = 0; LastError = null; NextAttemptAt = null; QuarantinedAt = null; }

    public static OutboxEvent ForNegotiation(Engagement engagement)
    {
        var id = Guid.CreateVersion7();
        var correlation = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
        var payload = new
        {
            messageId = id,
            type = "engagement.negotiation-opened.v1",
            schemaVersion = 1,
            occurredAt = engagement.CreatedAt,
            correlationId = correlation,
            traceParent = Activity.Current?.Id,
            causationId = (string?)null,
            data = new
            {
                engagementId = engagement.Id,
                clientId = engagement.ClientId,
                artistAccountId = engagement.ArtistAccountId,
                revision = 1,
                writingAllowed = true
            }
        };
        return new OutboxEvent
        {
            Id = id,
            EngagementId = engagement.Id,
            Type = "engagement.negotiation-opened.v1",
            Payload = JsonSerializer.Serialize(payload),
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
