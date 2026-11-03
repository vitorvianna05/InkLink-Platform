using InkLink.Engagements.Application;
using InkLink.Engagements.Application.Lifecycle;
using InkLink.Engagements.Domain;
using Microsoft.EntityFrameworkCore;
namespace InkLink.Engagements.Infrastructure.Persistence;

public sealed class EfLifecycleStore(EngagementsDbContext db) : ILifecycleStore
{
    private Task<Engagement?> ReadParticipant(Guid id, Guid actor, CancellationToken ct) =>
        db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && (x.ClientId == actor || x.ArtistAccountId == actor), ct);
    private Task<Engagement> Lock(Guid id, CancellationToken ct) =>
        db.Engagements.FromSqlInterpolated($"SELECT * FROM engagements.engagements WHERE \"Id\"={id} FOR UPDATE").SingleAsync(ct);
    public async Task<T> ChangeAsync<T>(Guid id, Guid participant, Func<Engagement, IReadOnlyList<LifecycleOperation>, LifecycleChange<T>> change, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var snapshot = await ReadParticipant(id, participant, ct) ?? throw new EngagementsFailure(EngagementsError.NotFound, "engagements.not_found");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({snapshot.ClientId.ToString() + snapshot.ArtistAccountId.ToString()}, 0))", ct);
        var engagement = await Lock(id, ct);
        var operations = await db.LifecycleOperations.Where(x => x.EngagementId == id).ToArrayAsync(ct);
        var mutation = change(engagement, operations);
        if (mutation.NewOperation is not null) db.LifecycleOperations.Add(mutation.NewOperation);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return mutation.Result;
    }
    public async Task<LifecycleStatus?> ReadAsync(Guid id, Guid participant, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        var e = await ReadParticipant(id, participant, ct);
        if (e is null) return null;
        var ops = await db.LifecycleOperations.AsNoTracking().Where(x => x.EngagementId == id).OrderBy(x => x.Id).ToArrayAsync(ct);
        return new(e.RequiresReservation, e.State.ToString(), ops.Select(View).ToArray());
    }
    public async Task<IReadOnlyList<PendingLifecycle>> PendingAsync(CancellationToken ct) =>
        await db.LifecycleOperations.AsNoTracking().Where(x => x.Status == "Pending" && (x.NextAttemptAt == null || x.NextAttemptAt <= DateTimeOffset.UtcNow))
            .OrderBy(x => x.UpdatedAt).Take(20).Select(x => new PendingLifecycle(x.Id, x.TraceParent, x.CorrelationId)).ToArrayAsync(ct);
    public async Task RunAsync(Guid id, Func<Engagement, LifecycleOperation, CancellationToken, Task> run, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Always lock the aggregate before operations: shared order with API requests and reservation receipts.
        var engagementId = await db.LifecycleOperations.AsNoTracking().Where(x => x.Id == id).Select(x => (Guid?)x.EngagementId).SingleOrDefaultAsync(ct);
        if (engagementId is null) return;
        var e = await Lock(engagementId.Value, ct);
        var op = await db.LifecycleOperations.SingleAsync(x => x.Id == id, ct);
        await run(e, op, ct);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    private static LifecycleView View(LifecycleOperation op) => new(op.Id, op.EngagementId, op.Kind, op.Status, op.Attempts, op.Error, op.UpdatedAt);
}
