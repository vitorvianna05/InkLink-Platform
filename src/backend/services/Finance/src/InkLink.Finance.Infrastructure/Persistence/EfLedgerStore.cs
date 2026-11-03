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
}
