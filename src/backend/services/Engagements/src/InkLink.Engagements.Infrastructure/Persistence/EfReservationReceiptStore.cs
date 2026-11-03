using System.Text.Json;
using InkLink.Engagements.Application.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace InkLink.Engagements.Infrastructure.Persistence;

public sealed class ReservationInbox
{
    public Guid Id { get; set; }
    public string Fingerprint { get; set; } = "";
    public DateTimeOffset ProcessedAt { get; set; }
}
public sealed class ReservationReceipt
{
    public Guid Id { get; set; }
    public Guid EngagementId { get; set; }
    public string Payload { get; set; } = "";
}
public sealed class EfReservationReceiptStore(EngagementsDbContext db) : IReservationReceiptStore
{
    public async Task ApplyAsync(ReservationEnvelope envelope, string fingerprint, Action<InkLink.Engagements.Domain.Engagement, ReservationNotice?, string?> validate, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(e => e.Id == envelope.Data.EngagementId, ct)
            ?? throw new InkLink.Engagements.Domain.InvalidEngagementData("engagement", "Unknown engagement.");
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO engagements.reservation_inbox (\"Id\",\"Fingerprint\",\"ProcessedAt\") VALUES ({envelope.MessageId},{fingerprint},{DateTimeOffset.UtcNow}) ON CONFLICT DO NOTHING", ct);
        var inbox = await db.ReservationInbox.AsNoTracking().SingleAsync(e => e.Id == envelope.MessageId, ct);
        if (inserted != 0)
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO engagements.reservation_receipts (\"Id\",\"EngagementId\",\"Payload\") VALUES ({envelope.Data.ReservationId},{envelope.Data.EngagementId},{JsonSerializer.Serialize(envelope.Data)}::jsonb) ON CONFLICT DO NOTHING", ct);
        var receipt = await db.ReservationReceipts.AsNoTracking().SingleOrDefaultAsync(r => r.Id == envelope.Data.ReservationId, ct);
        validate(engagement, receipt is null ? null : JsonSerializer.Deserialize<ReservationNotice>(receipt.Payload), inbox.Fingerprint);
        await tx.CommitAsync(ct);
    }
}
