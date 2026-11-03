using System.Data;
using InkLink.Scheduling.Application;
using InkLink.Scheduling.Application.Reservations;
using InkLink.Scheduling.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace InkLink.Scheduling.Infrastructure.Persistence;

public sealed class EfSchedulingStore(SchedulingDbContext db) : ISchedulingStore
{
    public async Task<ReservationView?> FindOperationAsync(Guid artist, Guid operation, CancellationToken ct)
    {
        var row = await db.Reservations.AsNoTracking().SingleOrDefaultAsync(r => r.ArtistId == artist && r.OperationId == operation, ct);
        return row is null ? null : View(row);
    }
    public async Task<ReservationView?> ReadAsync(Guid id, Guid participant, CancellationToken ct)
    {
        var row = await db.Reservations.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id && (r.ClientId == participant || r.ArtistId == participant), ct);
        return row is null ? null : View(row);
    }
    public async Task<Page<ReservationView>> ListAsync(Guid participant, string perspective, DateOnly from, DateOnly to, int limit, Guid? after, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var query = db.Reservations.AsNoTracking().Where(r => (perspective == "artist" ? r.ArtistId == participant : r.ClientId == participant) && r.LocalDate >= from && r.LocalDate < to);
        var total = await query.CountAsync(ct);
        if (after.HasValue)
        {
            var anchor = await query.SingleOrDefaultAsync(r => r.Id == after.Value, ct) ?? throw SchedulingFailure.Invalid("cursor", "Unknown cursor.");
            query = query.Where(r => r.Start > anchor.Start || (r.Start == anchor.Start && r.Id.CompareTo(anchor.Id) > 0));
        }
        var rows = await query.OrderBy(r => r.Start).ThenBy(r => r.Id).Take(limit + 1).ToArrayAsync(ct);
        return new(rows.Take(limit).Select(View).ToArray(), null, rows.Length > limit, total);
    }
    public async Task<AvailabilityView> AvailabilityAsync(Guid artist, DateOnly date, string defaultZone, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var calendar = await db.Calendars.AsNoTracking().SingleOrDefaultAsync(c => c.ArtistId == artist, ct);
        var windows = await db.Windows.AsNoTracking().Where(w => w.ArtistId == artist && w.Date == date).OrderBy(w => w.Start).Select(w => new Window(w.Start, w.End)).ToArrayAsync(ct);
        var busy = await db.Reservations.AsNoTracking().Where(r => r.ArtistId == artist && r.LocalDate == date).OrderBy(r => r.Start).Select(r => new BusyInterval(r.Start, r.End)).ToArrayAsync(ct);
        return new(artist, date, calendar?.TimeZoneId ?? defaultZone, windows, busy);
    }
    private async Task<ArtistCalendar> LockAsync(Guid artist, string zone, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO scheduling.calendars (\"ArtistId\",\"TimeZoneId\") VALUES ({artist},{zone}) ON CONFLICT DO NOTHING", ct);
        return await db.Calendars.FromSqlInterpolated($"SELECT * FROM scheduling.calendars WHERE \"ArtistId\"={artist} FOR UPDATE").SingleAsync(ct);
    }
    private async Task<CalendarSnapshot> SnapshotAsync(ArtistCalendar calendar, DateOnly date, CancellationToken ct)
    {
        var windows = await db.Windows.Where(w => w.ArtistId == calendar.ArtistId && w.Date == date).ToArrayAsync(ct);
        var reservations = await db.Reservations.AsNoTracking().Where(r => r.ArtistId == calendar.ArtistId && r.LocalDate == date).ToArrayAsync(ct);
        return new(calendar, windows, reservations, await db.Windows.AnyAsync(w => w.ArtistId == calendar.ArtistId, ct) || await db.Reservations.AnyAsync(r => r.ArtistId == calendar.ArtistId, ct));
    }
    public async Task ReplaceAvailabilityAsync(Guid artist, DateOnly date, string zone, Func<CalendarSnapshot, IReadOnlyList<AvailableWindow>> change, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var calendar = await LockAsync(artist, zone, ct);
        var snapshot = await SnapshotAsync(calendar, date, ct);
        var windows = change(snapshot);
        db.Windows.RemoveRange(snapshot.Windows.Where(w => w.Date == date)); db.Windows.AddRange(windows);
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
    }
    public async Task<Creation<ReservationView>> ConfirmAsync(Guid artist, Guid operation, DateOnly date, string defaultZone, Func<ReservationView, ReservationView> repeat, Func<CalendarSnapshot, Reservation> create, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var calendar = await LockAsync(artist, defaultZone, ct);
        var previous = await FindOperationAsync(artist, operation, ct);
        if (previous is not null) { var response = repeat(previous); await transaction.CommitAsync(ct); return new(response, false); }
        var reservation = create(await SnapshotAsync(calendar, date, ct));
        db.Reservations.Add(reservation); db.Outbox.Add(OutboxEvent.ForReservation(reservation));
        try { await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: "23P01" })
        { throw new SchedulingFailure(SchedulingError.Conflict, "scheduling.overlap"); }
        return new(View(reservation), true);
    }
    public static ReservationView View(Reservation r) => new(r.Id, r.OperationId, r.EngagementId, r.ClientId, r.ArtistId, r.Start, r.End, r.LocalDate, r.TimeZoneId, r.ClientName, r.ArtistName, r.Project, r.ProposalId, r.ReferenceAmount, r.ConfirmedAt);
}
