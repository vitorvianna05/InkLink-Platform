using InkLink.Finance.Domain;
namespace InkLink.Finance.Application.Ledger;

public sealed record EntryInput(string Kind, string Description, decimal Amount, DateOnly EffectiveDate, Guid? EngagementId = null);
public sealed record ReversalInput(string Reason);
public sealed record EntryView(Guid Id, Guid ArtistId, string Kind, string Description, decimal Amount, decimal SignedAmount,
    DateOnly EffectiveDate, Guid? EngagementId, Guid? OriginalEntryId, string TimeZoneId, DateTimeOffset CreatedAt, string Currency = "BRL");
public sealed record Creation<T>(T Value, bool Created);
public sealed record Page<T>(IReadOnlyList<T> Items, string? NextCursor, bool HasMore, int Total);
public sealed record LedgerTotals(decimal Received, decimal Costs);
public interface ILedgerStore
{
    Task<LedgerEntry?> FindOperationAsync(Guid artist, Guid key, CancellationToken ct);
    Task<Creation<LedgerEntry>> CreateAsync(LedgerEntry entry, CancellationToken ct);
    Task<Creation<LedgerEntry>> ReverseAsync(Guid artist, Guid id, Guid key, Func<LedgerEntry, LedgerEntry?, Creation<LedgerEntry>> change, CancellationToken ct);
    Task<LedgerEntry?> ReadAsync(Guid artist, Guid id, CancellationToken ct);
    Task<Page<LedgerEntry>> ListAsync(Guid artist, DateOnly from, DateOnly to, EntryKind? kind, int limit, Guid? after, CancellationToken ct);
    Task<LedgerTotals> TotalsAsync(Guid artist, DateOnly from, DateOnly to, CancellationToken ct);
}
public sealed record ArtistCalendarContext(Guid ArtistId, string TimeZoneId, DateTimeOffset ObservedAt);
public interface IFinanceSources
{
    Task<ArtistCalendarContext> CalendarAsync(CancellationToken ct);
    Task VerifyEngagementAsync(Guid id, Guid artist, CancellationToken ct);
}
