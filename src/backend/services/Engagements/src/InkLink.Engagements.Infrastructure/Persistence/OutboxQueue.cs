using Microsoft.EntityFrameworkCore;

namespace InkLink.Engagements.Infrastructure.Persistence;

public sealed class OutboxQueue(EngagementsDbContext database)
{
    // The caller must hold a transaction until the publication result is persisted.
    public Task<OutboxEvent?> LockNextAsync(CancellationToken ct) => database.Outbox.FromSqlRaw(
        "SELECT * FROM engagements.outbox WHERE \"PublishedAt\" IS NULL AND \"QuarantinedAt\" IS NULL AND (\"NextAttemptAt\" IS NULL OR \"NextAttemptAt\" <= now()) ORDER BY \"CreatedAt\", \"Id\" LIMIT 1 FOR UPDATE SKIP LOCKED").SingleOrDefaultAsync(ct);
}
