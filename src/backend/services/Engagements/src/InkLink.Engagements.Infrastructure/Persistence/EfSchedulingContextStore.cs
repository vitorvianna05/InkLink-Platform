using System.Data;
using System.Text.Json;
using InkLink.Engagements.Application.Negotiations;
using InkLink.Engagements.Application.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace InkLink.Engagements.Infrastructure.Persistence;

public sealed class EfSchedulingContextStore(EngagementsDbContext db) : ISchedulingContextStore
{
    public async Task<SchedulingContext?> ReadAsync(Guid id, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var e = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id, ct);
        if (e is null) return null;
        var proposal = e.CurrentProposalId.HasValue ? await db.Proposals.AsNoTracking().SingleOrDefaultAsync(p => p.Id == e.CurrentProposalId && p.EngagementId == id, ct) : null;
        var snapshot = JsonSerializer.Deserialize<CatalogSnapshot>(e.CatalogSnapshotJson) ?? throw new InvalidOperationException("Missing catalog snapshot.");
        return new(e.Id, e.ClientId, e.ArtistAccountId, e.State.ToString(), e.Idea, snapshot.Name, proposal?.Id, proposal?.State.ToString(), proposal?.Amount, e.Revision);
    }
}
