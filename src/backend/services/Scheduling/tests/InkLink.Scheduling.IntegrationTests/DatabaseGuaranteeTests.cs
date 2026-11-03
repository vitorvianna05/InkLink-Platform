using System.Net;
using InkLink.Scheduling.Domain;
using InkLink.Scheduling.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace InkLink.Scheduling.IntegrationTests;

[Collection("scheduling")]
public sealed class DatabaseGuaranteeTests(SchedulingFixture fixture)
{
    [Fact]
    public async Task Database_exclusion_rejects_concurrent_writers_even_without_calendar_lock()
    {
        var engagement = await Scenario.EngagementAsync(fixture); var day = Scenario.Day(); using var bob = Scenario.Client(fixture, "bob"); await Scenario.AvailableAsync(bob, day);
        await using var metadata = fixture.CreateOwnerContext();
        var calendar = await metadata.Calendars.AsNoTracking().SingleAsync(c => c.ArtistId == fixture.Base.Accounts["bob"]);
        var windows = await metadata.Windows.AsNoTracking().Where(w => w.ArtistId == calendar.ArtistId && w.Date == day).ToArrayAsync();
        async Task<bool> Attempt()
        {
            await using var db = fixture.CreateOwnerContext();
            var reservation = Reservation.Confirm(Guid.NewGuid(), engagement.Id, fixture.Base.Accounts["alice"], calendar, Guid.NewGuid(),
                Scenario.At(day, 10), Scenario.At(day, 11), "Alice", "Bob", "Project", 450, windows, DateTimeOffset.UtcNow);
            db.Reservations.Add(reservation);
            try { await db.SaveChangesAsync(); return true; }
            catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: "23P01" }) { return false; }
        }
        var results = await Task.WhenAll(Attempt(), Attempt()); Assert.Single(results, result => result); Assert.Single(results, result => !result);
    }
    [Fact]
    public async Task Reservation_and_outbox_roll_back_together_and_runtime_cannot_alter_schema()
    {
        var engagement = await Scenario.EngagementAsync(fixture); var day = Scenario.Day(); using var bob = Scenario.Client(fixture, "bob"); await Scenario.AvailableAsync(bob, day);
        await using var db = fixture.CreateOwnerContext();
        var calendar = await db.Calendars.SingleAsync(c => c.ArtistId == fixture.Base.Accounts["bob"]);
        var windows = await db.Windows.Where(w => w.ArtistId == calendar.ArtistId && w.Date == day).ToArrayAsync();
        var reservation = Reservation.Confirm(Guid.NewGuid(), engagement.Id, fixture.Base.Accounts["alice"], calendar, Guid.NewGuid(),
            Scenario.At(day, 10), Scenario.At(day, 11), "Alice", "Bob", "Project", 450, windows, DateTimeOffset.UtcNow);
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            db.Reservations.Add(reservation); db.Outbox.Add(OutboxEvent.ForReservation(reservation)); await db.SaveChangesAsync();
            Assert.True(await db.Outbox.AnyAsync(o => o.ReservationId == reservation.Id)); await tx.RollbackAsync();
        }
        db.ChangeTracker.Clear(); Assert.False(await db.Reservations.AnyAsync(r => r.Id == reservation.Id)); Assert.False(await db.Outbox.AnyAsync(o => o.ReservationId == reservation.Id));
        await using var connection = new NpgsqlConnection(fixture.AppConnection); await connection.OpenAsync();
        await using var ddl = new NpgsqlCommand("CREATE TABLE scheduling.forbidden(id int)", connection);
        Assert.Equal("42501", (await Assert.ThrowsAsync<PostgresException>(() => ddl.ExecuteNonQueryAsync())).SqlState);
        // Runtime cannot connect to another service's database.
        var cross = new NpgsqlConnectionStringBuilder(fixture.AppConnection) { Database = fixture.Base.CatalogDatabaseName };
        await using var forbidden = new NpgsqlConnection(cross.ConnectionString);
        Assert.Equal("42501", (await Assert.ThrowsAsync<PostgresException>(() => forbidden.OpenAsync())).SqlState);
    }
}
