using InkLink.Scheduling.Domain;
using Microsoft.EntityFrameworkCore;

namespace InkLink.Scheduling.Infrastructure.Persistence;

public sealed class SchedulingDbContext(DbContextOptions<SchedulingDbContext> options) : DbContext(options)
{
    public DbSet<ArtistCalendar> Calendars => Set<ArtistCalendar>();
    public DbSet<AvailableWindow> Windows => Set<AvailableWindow>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<OutboxEvent> Outbox => Set<OutboxEvent>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("scheduling");
        model.Entity<ArtistCalendar>(e => { e.ToTable("calendars"); e.HasKey(x => x.ArtistId); e.Property(x => x.ArtistId).ValueGeneratedNever(); e.Property(x => x.TimeZoneId).HasMaxLength(100); });
        model.Entity<AvailableWindow>(e =>
        {
            e.ToTable("availability", t => t.HasCheckConstraint("ck_availability_interval", "\"End\" > \"Start\""));
            e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever();
            e.HasIndex(x => new { x.ArtistId, x.Date }); e.HasOne<ArtistCalendar>().WithMany().HasForeignKey(x => x.ArtistId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<Reservation>(e =>
        {
            e.ToTable("reservations", t => t.HasCheckConstraint("ck_reservation_interval", "\"End\" > \"Start\" AND \"ArtistId\" <> \"ClientId\""));
            e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever();
            e.HasIndex(x => new { x.ArtistId, x.OperationId }).IsUnique();
            e.HasIndex(x => new { x.ArtistId, x.LocalDate, x.Start, x.Id }); e.HasIndex(x => new { x.ClientId, x.LocalDate, x.Start, x.Id });
            e.HasOne<ArtistCalendar>().WithMany().HasForeignKey(x => x.ArtistId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.TimeZoneId).HasMaxLength(100); e.Property(x => x.ClientName).HasMaxLength(200);
            e.Property(x => x.ArtistName).HasMaxLength(200); e.Property(x => x.Project).HasMaxLength(4000);
            e.Property(x => x.ReferenceAmount).HasPrecision(12, 2);
        });
        model.Entity<OutboxEvent>(e =>
        {
            e.ToTable("outbox"); e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Payload).HasColumnType("jsonb"); e.HasIndex(x => x.ReservationId).IsUnique();
            e.Property(x => x.LastError).HasMaxLength(200); e.HasIndex(x => new { x.PublishedAt, x.QuarantinedAt, x.NextAttemptAt });
        });
    }
}
