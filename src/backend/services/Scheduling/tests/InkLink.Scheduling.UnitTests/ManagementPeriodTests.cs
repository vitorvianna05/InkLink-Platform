using InkLink.Scheduling.Domain;

namespace InkLink.Scheduling.UnitTests;

public sealed class ManagementPeriodTests
{
    [Theory]
    [InlineData("America/Sao_Paulo", "2018-11-04", "2018-11-04T03:00:00Z", "2018-11-05T02:00:00Z")]
    [InlineData("America/Havana", "2026-11-01", "2026-11-01T04:00:00Z", "2026-11-02T05:00:00Z")]
    public void Civil_period_uses_first_existing_instant_and_earliest_repeated_midnight(string zone, string date, string start, string end)
    {
        var day = DateOnly.Parse(date);
        var period = ManagementPeriod.Create(day, day.AddDays(1), zone);
        Assert.Equal(DateTimeOffset.Parse(start), period.Start);
        Assert.Equal(DateTimeOffset.Parse(end), period.End);
        var next = ManagementPeriod.Create(day.AddDays(1), day.AddDays(2), zone);
        Assert.Equal(period.End, next.Start);
        var combined = ManagementPeriod.Create(day, day.AddDays(2), zone);
        Assert.Equal(period.Start, combined.Start);
        Assert.Equal(next.End, combined.End);
    }
}
