namespace InkLink.Scheduling.Domain;

public sealed class InvalidScheduleData(string field, string message) : Exception(message)
{
    public string Field { get; } = field;
}
public sealed class ReservedAvailabilityConflict : Exception;
public sealed record Window(TimeOnly Start, TimeOnly End);
public sealed class ArtistCalendar
{
    public const string DefaultTimeZone = "America/Sao_Paulo";
    private ArtistCalendar() { }
    public Guid ArtistId { get; private set; }
    public string TimeZoneId { get; private set; } = "";
    public static ArtistCalendar Create(Guid artist, string zone = DefaultTimeZone)
    {
        if (artist == Guid.Empty) throw new InvalidScheduleData("artistId", "Artist required.");
        Zone(zone);
        return new ArtistCalendar { ArtistId = artist, TimeZoneId = zone };
    }
    public static TimeZoneInfo Zone(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || !TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out _))
            throw new InvalidScheduleData("timeZoneId", "Use an IANA time zone.");
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        { throw new InvalidScheduleData("timeZoneId", "Unknown time zone."); }
    }
    public void SelectZone(string zone, bool hasEntries)
    {
        Zone(zone);
        if (zone != TimeZoneId && hasEntries) throw new InvalidScheduleData("timeZoneId", "Cannot change a calendar time zone with existing entries.");
        TimeZoneId = zone;
    }
    public IReadOnlyList<AvailableWindow> Define(DateOnly date, IReadOnlyList<Window>? windows, IReadOnlyList<Reservation>? reservations = null)
    {
        if (windows is null || windows.Count > 100 || windows.Any(w => w is null)) throw new InvalidScheduleData("windows", "Use up to 100 windows.");
        var result = windows.Select(window => AvailableWindow.Create(ArtistId, date, window, TimeZoneId)).OrderBy(w => w.Start).ToArray();
        for (var i = 1; i < result.Length; i++)
            if (result[i].Start < result[i - 1].End) throw new InvalidScheduleData("windows", "Windows must not overlap.");
        foreach (var reservation in reservations ?? [])
        {
            if (reservation.ArtistId != ArtistId || reservation.LocalDate != date) continue;
            var zone = Zone(TimeZoneId);
            var interval = LocalInterval(TimeZoneInfo.ConvertTime(reservation.Start, zone), TimeZoneInfo.ConvertTime(reservation.End, zone));
            if (!result.Any(window => window.Covers(interval.Start, interval.End))) throw new ReservedAvailabilityConflict();
        }
        return result;
    }
    public (DateOnly Date, TimeOnly Start, TimeOnly End) LocalInterval(DateTimeOffset start, DateTimeOffset end)
    {
        if (start.Second != 0 || start.Ticks % TimeSpan.TicksPerMinute != 0 || end.Ticks % TimeSpan.TicksPerMinute != 0 || end <= start)
            throw new InvalidScheduleData("interval", "Use whole minutes and end after start.");
        var zone = Zone(TimeZoneId);
        var a = TimeZoneInfo.ConvertTime(start, zone); var z = TimeZoneInfo.ConvertTime(end, zone);
        if (start.Offset != a.Offset || end.Offset != z.Offset || a.Date != z.Date)
            throw new InvalidScheduleData("interval", "Offsets must match the artist zone; use a single local date.");
        CheckLocal(a.DateTime, zone); CheckLocal(z.DateTime, zone);
        return (DateOnly.FromDateTime(a.DateTime), TimeOnly.FromDateTime(a.DateTime), TimeOnly.FromDateTime(z.DateTime));
    }
    internal static void CheckLocal(DateTime local, TimeZoneInfo zone)
    {
        if (zone.IsInvalidTime(local) || zone.IsAmbiguousTime(local))
            throw new InvalidScheduleData("interval", "Ambiguous or nonexistent local time.");
    }
}
public sealed class AvailableWindow
{
    private AvailableWindow() { }
    public Guid Id { get; private set; }
    public Guid ArtistId { get; private set; }
    public DateOnly Date { get; private set; }
    public TimeOnly Start { get; private set; }
    public TimeOnly End { get; private set; }
    public static AvailableWindow Create(Guid artist, DateOnly date, Window window, string zone)
    {
        if (window.Start.Ticks % TimeSpan.TicksPerMinute != 0 || window.End.Ticks % TimeSpan.TicksPerMinute != 0 || window.End <= window.Start)
            throw new InvalidScheduleData("windows", "Use whole minutes and end after start.");
        var timezone = ArtistCalendar.Zone(zone);
        ArtistCalendar.CheckLocal(date.ToDateTime(window.Start), timezone); ArtistCalendar.CheckLocal(date.ToDateTime(window.End), timezone);
        return new AvailableWindow { Id = Guid.CreateVersion7(), ArtistId = artist, Date = date, Start = window.Start, End = window.End };
    }
    public bool Covers(TimeOnly start, TimeOnly end) => Start <= start && End >= end;
}
public sealed class Reservation
{
    private Reservation() { }
    public Guid Id { get; private set; }
    public Guid OperationId { get; private set; }
    public Guid EngagementId { get; private set; }
    public Guid ClientId { get; private set; }
    public Guid ArtistId { get; private set; }
    public DateTimeOffset Start { get; private set; }
    public DateTimeOffset End { get; private set; }
    public DateOnly LocalDate { get; private set; }
    public string TimeZoneId { get; private set; } = "";
    public string ClientName { get; private set; } = "";
    public string ArtistName { get; private set; } = "";
    public string Project { get; private set; } = "";
    public Guid ProposalId { get; private set; }
    public decimal ReferenceAmount { get; private set; }
    public DateTimeOffset ConfirmedAt { get; private set; }
    public static Reservation Confirm(Guid operation, Guid engagement, Guid client, ArtistCalendar calendar, Guid proposal,
        DateTimeOffset start, DateTimeOffset end, string clientName, string artistName, string project, decimal amount,
        IReadOnlyList<AvailableWindow> windows, DateTimeOffset now)
    {
        if (operation == Guid.Empty || engagement == Guid.Empty || client == Guid.Empty || client == calendar.ArtistId || proposal == Guid.Empty
            || string.IsNullOrWhiteSpace(clientName) || string.IsNullOrWhiteSpace(artistName) || string.IsNullOrWhiteSpace(project) || amount <= 0)
            throw new InvalidScheduleData("reservation", "Complete reservation context required.");
        var interval = calendar.LocalInterval(start, end);
        if (start <= now) throw new InvalidScheduleData("start", "Start must be in the future.");
        if (!windows.Any(w => w.ArtistId == calendar.ArtistId && w.Date == interval.Date && w.Covers(interval.Start, interval.End)))
            throw new InvalidScheduleData("interval", "Outside artist availability.");
        return new Reservation
        {
            Id = Guid.CreateVersion7(),
            OperationId = operation,
            EngagementId = engagement,
            ClientId = client,
            ArtistId = calendar.ArtistId,
            ProposalId = proposal,
            Start = start.ToUniversalTime(),
            End = end.ToUniversalTime(),
            LocalDate = interval.Date,
            TimeZoneId = calendar.TimeZoneId,
            ClientName = clientName,
            ArtistName = artistName,
            Project = project,
            ReferenceAmount = amount,
            ConfirmedAt = new DateTimeOffset(now.UtcTicks - now.UtcTicks % 10, TimeSpan.Zero)
        };
    }
}
