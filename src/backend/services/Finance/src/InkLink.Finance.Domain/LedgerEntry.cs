namespace InkLink.Finance.Domain;

public sealed class InvalidFinanceData(string field, string message) : Exception(message)
{
    public string Field { get; } = field;
}
public enum EntryKind { Receipt, Cost }
public sealed class LedgerEntry
{
    private LedgerEntry() { }
    public Guid Id { get; private set; }
    public Guid ArtistId { get; private set; }
    public EntryKind Kind { get; private set; }
    public string Description { get; private set; } = "";
    public decimal Amount { get; private set; }
    public DateOnly EffectiveDate { get; private set; }
    public Guid? EngagementId { get; private set; }
    public Guid? OriginalEntryId { get; private set; }
    public Guid RequestKey { get; private set; }
    public string Fingerprint { get; private set; } = "";
    public string TimeZoneId { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public static string Text(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 4000) throw new InvalidFinanceData(field, "Required text, at most 4000 characters.");
        return value.Trim();
    }
    public static decimal Money(decimal amount)
    {
        if (amount <= 0 || amount > 9999999.99m || decimal.Round(amount, 2) != amount)
            throw new InvalidFinanceData("amount", "Use a positive BRL amount up to 9999999.99 with two decimal places.");
        return amount;
    }
    public static LedgerEntry Record(Guid artist, Guid key, string fingerprint, EntryKind kind, string description, decimal amount,
        DateOnly date, Guid? engagement, string zone, DateOnly today, DateTimeOffset now)
    {
        if (artist == Guid.Empty || key == Guid.Empty || engagement == Guid.Empty) throw new InvalidFinanceData("id", "Nonempty UUID required.");
        if (!Enum.IsDefined(kind)) throw new InvalidFinanceData("kind", "Use Receipt or Cost.");
        if (kind == EntryKind.Receipt && engagement is null) throw new InvalidFinanceData("engagementId", "Receipt requires an engagement.");
        if (date == default || date > today) throw new InvalidFinanceData("effectiveDate", "Effective date must not be in the future.");
        return new()
        {
            Id = Guid.CreateVersion7(),
            ArtistId = artist,
            RequestKey = key,
            Fingerprint = fingerprint,
            Kind = kind,
            Description = Text(description, "description"),
            Amount = Money(amount),
            EffectiveDate = date,
            EngagementId = engagement,
            TimeZoneId = zone,
            CreatedAt = Instant(now)
        };
    }
    public LedgerEntry Reverse(Guid key, string fingerprint, string reason, DateTimeOffset now)
    {
        if (OriginalEntryId is not null) throw new InvalidFinanceData("entryId", "A reversal cannot be reversed.");
        if (key == Guid.Empty) throw new InvalidFinanceData("idempotencyKey", "Nonempty UUID required.");
        return new()
        {
            Id = Guid.CreateVersion7(),
            ArtistId = ArtistId,
            Kind = Kind,
            Description = Text(reason, "reason"),
            Amount = Amount,
            EffectiveDate = EffectiveDate,
            EngagementId = EngagementId,
            OriginalEntryId = Id,
            RequestKey = key,
            Fingerprint = fingerprint,
            TimeZoneId = TimeZoneId,
            CreatedAt = Instant(now)
        };
    }
    private static DateTimeOffset Instant(DateTimeOffset value) => new(value.UtcTicks - value.UtcTicks % 10, TimeSpan.Zero);
}
public sealed record FinancialIndicators(decimal Received, decimal Costs, decimal EstimatedProfit, decimal EstimatedMargin)
{
    public static FinancialIndicators Calculate(decimal received, decimal costs) =>
        new(received, costs, received - costs, received == 0 ? 0 : decimal.Round((received - costs) / received * 100, 2, MidpointRounding.AwayFromZero));
}
