using InkLink.Scheduling.Domain;

namespace InkLink.Scheduling.UnitTests;

public sealed class CalendarTests
{
    [Fact]
    public void Availability_accepts_adjacent_windows_and_rejects_overlap()
    {
        var calendar = ArtistCalendar.Create(Guid.NewGuid(), "America/Sao_Paulo");
        var date = new DateOnly(2030, 1, 15);
        Assert.Equal(2, calendar.Define(date, [new(new(9, 0), new(10, 0)), new(new(10, 0), new(11, 0))]).Count);
        Assert.Throws<InvalidScheduleData>(() => calendar.Define(date, [new(new(9, 0), new(10, 0)), new(new(9, 30), new(11, 0))]));
    }
    [Theory]
    [InlineData("2030-01-15T09:00:01-03:00", "2030-01-15T10:00:00-03:00")]
    [InlineData("2030-01-15T09:00:00-03:00", "2030-01-15T09:00:00-03:00")]
    [InlineData("2030-01-15T23:00:00-03:00", "2030-01-16T01:00:00-03:00")]
    [InlineData("2030-01-15T09:00:00+00:00", "2030-01-15T10:00:00+00:00")]
    public void Reservation_interval_rejects_invalid_precision_date_and_offset(string start, string end) =>
        Assert.Throws<InvalidScheduleData>(() => ArtistCalendar.Create(Guid.NewGuid(), "America/Sao_Paulo").LocalInterval(DateTimeOffset.Parse(start), DateTimeOffset.Parse(end)));
    [Theory]
    [InlineData(2030, 3, 10, 2, 30)]
    [InlineData(2030, 11, 3, 1, 30)]
    public void Dst_gaps_and_ambiguous_times_are_rejected(int year, int month, int day, int hour, int minute)
    {
        var calendar = ArtistCalendar.Create(Guid.NewGuid(), "America/New_York");
        Assert.Throws<InvalidScheduleData>(() => calendar.Define(new(year, month, day), [new(new(hour, minute), new(4, 0))]));
    }
    [Fact]
    public void Calendar_zone_changes_require_an_empty_calendar()
    {
        var calendar = ArtistCalendar.Create(Guid.NewGuid(), "America/Sao_Paulo");
        Assert.Throws<InvalidScheduleData>(() => calendar.SelectZone("Etc/UTC", true));
        calendar.SelectZone("Etc/UTC", false); Assert.Equal("Etc/UTC", calendar.TimeZoneId);
    }
    [Fact]
    public void Domain_preserves_reserved_windows_and_owns_the_default_zone()
    {
        var calendar = ArtistCalendar.Create(Guid.NewGuid());
        Assert.Equal(ArtistCalendar.DefaultTimeZone, calendar.TimeZoneId);
        var date = new DateOnly(2030, 1, 15);
        var windows = calendar.Define(date, [new(new(9, 0), new(12, 0))]);
        var reservation = Reservation.Confirm(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), calendar, Guid.NewGuid(),
            new(2030, 1, 15, 10, 0, 0, TimeSpan.FromHours(-3)), new(2030, 1, 15, 11, 0, 0, TimeSpan.FromHours(-3)),
            "Client", "Artist", "Project", 450, windows, new(2029, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.Throws<ReservedAvailabilityConflict>(() => calendar.Define(date, [], [reservation]));
        Assert.Single(calendar.Define(date, [new(new(10, 0), new(11, 0))], [reservation]));
    }
}
