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
        if (input.Kind != "Receipt") throw FinanceFailure.Invalid("kind", "Use Receipt.");
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
    public async Task<EntryView> ReadAsync(Guid id, CancellationToken ct) =>
        View(await store.ReadAsync(AccountAccess.Require(current, artist: true).Id, id, ct) ?? throw NotFound());
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
