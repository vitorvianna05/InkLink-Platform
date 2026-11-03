using InkLink.Scheduling.Application.Accounts;
using InkLink.Scheduling.Domain;
namespace InkLink.Scheduling.Application.Reservations;

public sealed record CompletionInput(DateTimeOffset CompletedAt, decimal? Amount = null);
public sealed record CompletionView(Guid ReservationId, Guid ArtistId, Guid ClientId, Guid EngagementId, DateTimeOffset CompletedAt, decimal Amount, string Currency = "BRL");
public sealed record ArtistScheduleSummary(Guid ArtistId, string TimeZoneId, int CompletedSessions, int ClientsServed, decimal CompletedValue,
    int ScheduledReservations, decimal ScheduledValue, DateTimeOffset ObservedAt);
public sealed record ArtistCalendarContext(Guid ArtistId, string TimeZoneId, DateTimeOffset ObservedAt);
public interface ISessionStore
{
    Task<SessionCompletion> CompleteAsync(Guid id, Guid artist, Func<Reservation, SessionCompletion?, SessionCompletion> change, CancellationToken ct);
    Task<SessionCompletion?> ReadAsync(Guid id, Guid participant, CancellationToken ct);
    Task<string?> ZoneAsync(Guid artist, CancellationToken ct);
    Task<ArtistScheduleSummary> SummaryAsync(Guid artist, DateOnly from, DateOnly to, string zone, ManagementPeriod period, DateTimeOffset now, CancellationToken ct);
}
public sealed class ManageSessions(ICurrentSchedulingAccount current, ISessionStore store, TimeProvider clock)
{
    public async Task<CompletionView> CompleteAsync(Guid id, CompletionInput input, CancellationToken ct)
    {
        var artist = AccountAccess.Require(current, artist: true).Id;
        var result = await store.CompleteAsync(id, artist, (reservation, previous) =>
        {
            var amount = SessionCompletion.Value(input.Amount ?? reservation.ReferenceAmount);
            var instant = SessionCompletion.Instant(input.CompletedAt);
            if (previous is not null)
            {
                if (previous.CompletedAt != instant || previous.Amount != amount)
                    throw new SchedulingFailure(SchedulingError.Conflict, "scheduling.completion_conflict");
                return previous;
            }
            return SessionCompletion.Finish(reservation, instant, amount, clock.GetUtcNow());
        }, ct);
        return View(result);
    }
    public async Task<CompletionView> ReadAsync(Guid id, CancellationToken ct) =>
        View(await store.ReadAsync(id, AccountAccess.Require(current).Id, ct) ?? throw ManageSchedule.NotFound());
    public async Task<ArtistCalendarContext> CalendarAsync(CancellationToken ct)
    {
        var artist = AccountAccess.Require(current, artist: true).Id;
        return new(artist, await store.ZoneAsync(artist, ct) ?? ArtistCalendar.DefaultTimeZone, clock.GetUtcNow());
    }
    public async Task<ArtistScheduleSummary> SummaryAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var artist = AccountAccess.Require(current, artist: true).Id;
        var zone = await store.ZoneAsync(artist, ct) ?? ArtistCalendar.DefaultTimeZone;
        var period = ManagementPeriod.Create(from, to, zone);
        return await store.SummaryAsync(artist, from, to, zone, period, clock.GetUtcNow(), ct);
    }
    private static CompletionView View(SessionCompletion c) => new(c.ReservationId, c.ArtistId, c.ClientId, c.EngagementId, c.CompletedAt, c.Amount);
}
