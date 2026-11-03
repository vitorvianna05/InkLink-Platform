namespace InkLink.Scheduling.Domain;

public sealed class SessionCompletion
{
    private SessionCompletion() { }
    public Guid ReservationId { get; private set; }
    public Guid ArtistId { get; private set; }
    public Guid ClientId { get; private set; }
    public Guid EngagementId { get; private set; }
    public DateTimeOffset CompletedAt { get; private set; }
    public decimal Amount { get; private set; }
    public static DateTimeOffset Instant(DateTimeOffset value) => new(value.UtcTicks - value.UtcTicks % 10, TimeSpan.Zero);
    public static decimal Value(decimal amount)
    {
        if (amount <= 0 || amount > 9999999.99m || decimal.Round(amount, 2) != amount)
            throw new InvalidScheduleData("amount", "Positive BRL value up to 9999999.99, with two decimal places.");
        return amount;
    }
    public static SessionCompletion Finish(Reservation reservation, DateTimeOffset completedAt, decimal amount, DateTimeOffset now)
    {
        completedAt = Instant(completedAt);
        if (now < reservation.End || completedAt < reservation.End || completedAt > now)
            throw new InvalidScheduleData("completedAt", "Complete only after the reserved end, with a completion instant between end and now.");
        return new()
        {
            ReservationId = reservation.Id,
            ArtistId = reservation.ArtistId,
            ClientId = reservation.ClientId,
            EngagementId = reservation.EngagementId,
            CompletedAt = completedAt,
            Amount = Value(amount)
        };
    }
}
public sealed record ManagementPeriod(DateTimeOffset Start, DateTimeOffset End)
{
    public static ManagementPeriod Create(DateOnly from, DateOnly to, string zone)
    {
        if (from == default || to <= from || to.DayNumber - from.DayNumber > 366)
            throw new InvalidScheduleData("period", "Use a [from,to) range of 1..366 days.");
        var timeZone = ArtistCalendar.Zone(zone);
        DateTimeOffset Boundary(DateOnly date)
        {
            var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            if (timeZone.IsInvalidTime(local))
            {
                // Use the first valid minute, matching scheduling precision and avoiding sub-millisecond transition artifacts.
                do { local = local.AddMinutes(1); } while (timeZone.IsInvalidTime(local));
            }
            // Repeated midnight belongs to the day from its earliest occurrence in UTC.
            if (timeZone.IsAmbiguousTime(local))
                return new DateTimeOffset(local, timeZone.GetAmbiguousTimeOffsets(local).Max()).ToUniversalTime();
            return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, timeZone), TimeSpan.Zero);
        }
        return new(Boundary(from), Boundary(to));
    }
}
