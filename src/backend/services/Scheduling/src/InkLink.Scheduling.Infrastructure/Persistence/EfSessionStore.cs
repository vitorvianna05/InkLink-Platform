using System.Data;
using InkLink.Scheduling.Application.Reservations;
using InkLink.Scheduling.Domain;
using Microsoft.EntityFrameworkCore;
namespace InkLink.Scheduling.Infrastructure.Persistence;

public sealed class EfSessionStore(SchedulingDbContext db) : ISessionStore
{
    public async Task<SessionCompletion> CompleteAsync(Guid id, Guid artist, Func<Reservation, SessionCompletion?, SessionCompletion> change, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var reservation = await db.Reservations.FromSqlInterpolated($"SELECT * FROM scheduling.reservations WHERE \"Id\"={id} AND \"ArtistId\"={artist} FOR UPDATE").SingleOrDefaultAsync(ct)
            ?? throw ManageSchedule.NotFound();
        var previous = await db.CompletedSessions.SingleOrDefaultAsync(x => x.ReservationId == id, ct);
        var result = change(reservation, previous);
        if (previous is null) db.CompletedSessions.Add(result);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return result;
    }
    public Task<SessionCompletion?> ReadAsync(Guid id, Guid participant, CancellationToken ct) =>
        db.CompletedSessions.AsNoTracking().SingleOrDefaultAsync(x => x.ReservationId == id && (x.ArtistId == participant || x.ClientId == participant), ct);
    public Task<string?> ZoneAsync(Guid artist, CancellationToken ct) =>
        db.Calendars.AsNoTracking().Where(x => x.ArtistId == artist).Select(x => x.TimeZoneId).SingleOrDefaultAsync(ct);
    public async Task<ArtistScheduleSummary> SummaryAsync(Guid artist, DateOnly from, DateOnly to, string zone, ManagementPeriod period, DateTimeOffset now, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var completed = db.CompletedSessions.AsNoTracking().Where(x => x.ArtistId == artist && x.CompletedAt >= period.Start && x.CompletedAt < period.End);
        var count = await completed.CountAsync(ct);
        var clients = await completed.Select(x => x.ClientId).Distinct().CountAsync(ct);
        var value = await completed.SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
        var scheduled = db.Reservations.AsNoTracking().Where(x => x.ArtistId == artist && x.LocalDate >= from && x.LocalDate < to
            && !db.CompletedSessions.Any(c => c.ReservationId == x.Id));
        return new(artist, zone, count, clients, value, await scheduled.CountAsync(ct), await scheduled.SumAsync(x => (decimal?)x.ReferenceAmount, ct) ?? 0, now);
    }
}
