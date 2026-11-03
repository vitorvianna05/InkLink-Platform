using InkLink.Engagements.Domain;
using Microsoft.EntityFrameworkCore;

namespace InkLink.Engagements.Infrastructure.Persistence;

public sealed class EngagementsDbContext(DbContextOptions<EngagementsDbContext> options) : DbContext(options)
{
    public DbSet<Engagement> Engagements => Set<Engagement>();
    public DbSet<Proposal> Proposals => Set<Proposal>();
    public DbSet<OutboxEvent> Outbox => Set<OutboxEvent>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("engagements");
        model.Entity<Engagement>(entity =>
        {
            entity.ToTable("engagements", table =>
            {
                table.HasCheckConstraint("ck_distinct_participants", "\"ClientId\" <> \"ArtistAccountId\"");
                table.HasCheckConstraint("ck_size", "\"ApproximateSizeCm\" > 0 AND \"ApproximateSizeCm\" <= 300");
            });
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.HasIndex(e => new { e.ClientId, e.RequestKey }).IsUnique();
            entity.HasIndex(e => new { e.ClientId, e.Id });
            entity.HasIndex(e => new { e.ArtistAccountId, e.Id });
            entity.Property(e => e.Idea).HasMaxLength(4000).IsRequired();
            entity.Property(e => e.BodyRegion).HasMaxLength(120).IsRequired();
            entity.Property(e => e.ApproximateSizeCm).HasPrecision(5, 2);
            entity.Property(e => e.CatalogSnapshotJson).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.RequestFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(e => e.State).HasConversion<string>().HasMaxLength(20);
        });
        model.Entity<Proposal>(entity =>
        {
            entity.ToTable("proposals", table => table.HasCheckConstraint("ck_amount", "\"Amount\" > 0"));
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Id).ValueGeneratedNever();
            entity.HasOne<Engagement>().WithMany().HasForeignKey(p => p.EngagementId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(p => new { p.EngagementId, p.RequestKey }).IsUnique();
            entity.Property(p => p.Description).HasMaxLength(4000).IsRequired();
            entity.Property(p => p.Amount).HasPrecision(9, 2);
            entity.Property(p => p.RequestFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(p => p.State).HasConversion<string>().HasMaxLength(20);
        });
        model.Entity<OutboxEvent>(entity =>
        {
            entity.ToTable("outbox");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Type).HasMaxLength(120).IsRequired();
            entity.Property(e => e.Payload).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.LastError).HasMaxLength(200);
            entity.HasIndex(e => new { e.EngagementId, e.Type }).IsUnique();
            entity.HasIndex(e => new { e.PublishedAt, e.CreatedAt });
        });
    }
}
