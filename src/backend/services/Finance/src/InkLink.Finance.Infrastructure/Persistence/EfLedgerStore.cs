using System.Data;
using InkLink.Finance.Application;
using InkLink.Finance.Application.Ledger;
using InkLink.Finance.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;
namespace InkLink.Finance.Infrastructure.Persistence;

public sealed class EfLedgerStore(FinanceDbContext db) : ILedgerStore
{
    public Task<LedgerEntry?> FindOperationAsync(Guid artist, Guid key, CancellationToken ct) =>
        db.Entries.AsNoTracking().SingleOrDefaultAsync(x => x.ArtistId == artist && x.RequestKey == key, ct);
    public Task<LedgerEntry?> ReadAsync(Guid artist, Guid id, CancellationToken ct) =>
        db.Entries.AsNoTracking().SingleOrDefaultAsync(x => x.ArtistId == artist && x.Id == id, ct);
    public async Task<Creation<LedgerEntry>> CreateAsync(LedgerEntry entry, CancellationToken ct)
    {
        db.Entries.Add(entry);
        try { await db.SaveChangesAsync(ct); return new(entry, true); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            db.ChangeTracker.Clear();
            return new(await FindOperationAsync(entry.ArtistId, entry.RequestKey, ct) ?? throw new FinanceFailure(FinanceError.Conflict, "finance.integrity_conflict"), false);
        }
    }
    public async Task<Creation<LedgerEntry>> ReverseAsync(Guid artist, Guid id, Guid key,
        Func<LedgerEntry, LedgerEntry?, Creation<LedgerEntry>> change, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var original = await db.Entries.FromSqlInterpolated($"SELECT * FROM finance.entries WHERE \"Id\"={id} AND \"ArtistId\"={artist} FOR UPDATE").SingleOrDefaultAsync(ct) ?? throw ManageLedger.NotFound();
        var reversal = await db.Entries.SingleOrDefaultAsync(x => x.OriginalEntryId == id, ct);
        var result = change(original, reversal);
        if (result.Created) db.Entries.Add(result.Value);
        try { await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return result; }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            await tx.RollbackAsync(ct); db.ChangeTracker.Clear();
            return new(await FindOperationAsync(artist, key, ct) ?? throw new FinanceFailure(FinanceError.Conflict, "finance.integrity_conflict"), false);
        }
    }
    public async Task<Page<LedgerEntry>> ListAsync(Guid artist, DateOnly from, DateOnly to, EntryKind? kind, int limit, Guid? after, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var query = db.Entries.AsNoTracking().Where(x => x.ArtistId == artist && x.EffectiveDate >= from && x.EffectiveDate < to && (kind == null || x.Kind == kind));
        var total = await query.CountAsync(ct);
        if (after.HasValue) query = query.Where(x => x.Id.CompareTo(after.Value) > 0);
        var rows = await query.OrderBy(x => x.Id).Take(limit + 1).ToArrayAsync(ct);
        return new(rows.Take(limit).ToArray(), null, rows.Length > limit, total);
    }
}
