using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using InkLink.Finance.Application.Accounts;
using InkLink.Finance.Domain;
namespace InkLink.Finance.Application.Ledger;

public sealed class ManageLedger(ICurrentFinanceAccount current, ILedgerStore store, IFinanceSources sources, TimeProvider clock)
{
    public async Task<Creation<EntryView>> RecordAsync(EntryInput input, Guid key, CancellationToken ct)
    {
        var artist = AccountAccess.Require(current, artist: true).Id;
        Key(key);
        if (input.Kind is not ("Receipt" or "Cost")) throw FinanceFailure.Invalid("kind", "Use Receipt or Cost.");
        var kind = Enum.Parse<EntryKind>(input.Kind);
        var description = LedgerEntry.Text(input.Description, "description"); LedgerEntry.Money(input.Amount);
        if (input.EngagementId == Guid.Empty || (kind == EntryKind.Receipt && input.EngagementId is null))
            throw FinanceFailure.Invalid("engagementId", "Receipt requires a nonempty engagement UUID.");
        var fingerprint = Fingerprint(new
        {
            Operation = "record",
            input.Kind,
            Description = description,
            Amount = input.Amount.ToString("G29", CultureInfo.InvariantCulture),
            input.EffectiveDate,
            input.EngagementId
        });
        var previous = await store.FindOperationAsync(artist, key, ct);
        if (previous is not null) { Same(previous, fingerprint); return new(View(previous), false); }
        var now = clock.GetUtcNow();
        var schedule = await sources.CalendarAsync(ct);
        VerifyArtist(schedule.ArtistId, artist);
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZoneId); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { throw Unavailable(); }
        if (input.EngagementId.HasValue) await sources.VerifyEngagementAsync(input.EngagementId.Value, artist, ct);
        var entry = LedgerEntry.Record(artist, key, fingerprint, kind, description, input.Amount, input.EffectiveDate, input.EngagementId,
            schedule.TimeZoneId, DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime), now);
        var result = await store.CreateAsync(entry, ct); Same(result.Value, fingerprint);
        return new(View(result.Value), result.Created);
    }
    public async Task<Creation<EntryView>> ReverseAsync(Guid id, ReversalInput input, Guid key, CancellationToken ct)
    {
        var artist = AccountAccess.Require(current, artist: true).Id;
        Key(key); var reason = LedgerEntry.Text(input.Reason, "reason");
        var fingerprint = Fingerprint(new { Operation = "reverse", Id = id, Reason = reason });
        var previous = await store.FindOperationAsync(artist, key, ct);
        if (previous is not null) { Same(previous, fingerprint); return new(View(previous), false); }
        var result = await store.ReverseAsync(artist, id, key, (original, reversal) =>
        {
            if (reversal is not null)
            {
                if (reversal.RequestKey != key) throw new FinanceFailure(FinanceError.Conflict, "finance.already_reversed");
                Same(reversal, fingerprint); return new(reversal, false);
            }
            return new(original.Reverse(key, fingerprint, reason, clock.GetUtcNow()), true);
        }, ct);
        Same(result.Value, fingerprint); return new(View(result.Value), result.Created);
    }
    public async Task<EntryView> ReadAsync(Guid id, CancellationToken ct) =>
        View(await store.ReadAsync(AccountAccess.Require(current, artist: true).Id, id, ct) ?? throw NotFound());
    public async Task<Page<EntryView>> ListAsync(DateOnly from, DateOnly to, string? kind, int limit, string? cursor, CancellationToken ct)
    {
        var artist = AccountAccess.Require(current, artist: true).Id; Range(from, to);
        if (limit is < 1 or > 100 || kind is not (null or "Receipt" or "Cost")) throw FinanceFailure.Invalid("query", "Use limit 1..100 and Receipt/Cost.");
        var prefix = $"v1|{artist}|{from:yyyy-MM-dd}|{to:yyyy-MM-dd}|{kind}|"; Guid? after = null;
        if (cursor is not null)
        {
            try
            {
                if (cursor.Length > 500) throw new FormatException();
                var value = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
                if (!value.StartsWith(prefix, StringComparison.Ordinal) || !Guid.TryParse(value[prefix.Length..], out var id) || id == Guid.Empty) throw new FormatException();
                after = id;
            }
            catch (FormatException) { throw FinanceFailure.Invalid("cursor", "Cursor does not match query."); }
        }
        var page = await store.ListAsync(artist, from, to, kind is null ? null : Enum.Parse<EntryKind>(kind), limit, after, ct);
        return new(page.Items.Select(View).ToArray(), page.HasMore ? Convert.ToBase64String(Encoding.UTF8.GetBytes(prefix + page.Items[^1].Id)) : null, page.HasMore, page.Total);
    }
    public static void Range(DateOnly from, DateOnly to)
    {
        if (from == default || to <= from || to.DayNumber - from.DayNumber > 366) throw FinanceFailure.Invalid("period", "Use a [from,to) period of 1..366 days.");
    }
    public static EntryView View(LedgerEntry e) => new(e.Id, e.ArtistId, e.Kind.ToString(), e.Description, e.Amount,
        e.OriginalEntryId.HasValue ? -e.Amount : e.Amount, e.EffectiveDate, e.EngagementId, e.OriginalEntryId, e.TimeZoneId, e.CreatedAt);
    public static FinanceFailure NotFound() => new(FinanceError.NotFound, "finance.not_found");
    public static FinanceFailure Unavailable() => new(FinanceError.Unavailable, "finance.source_unavailable");
    private static void VerifyArtist(Guid actual, Guid expected) { if (actual != expected) throw Unavailable(); }
    private static void Key(Guid key) { if (key == Guid.Empty) throw FinanceFailure.Invalid("idempotencyKey", "Nonempty UUID required."); }
    private static string Fingerprint<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    private static void Same(LedgerEntry entry, string fingerprint)
    { if (entry.Fingerprint != fingerprint) throw new FinanceFailure(FinanceError.Conflict, "finance.idempotency_conflict"); }
}
