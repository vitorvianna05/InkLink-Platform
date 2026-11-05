using InkLink.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace InkLink.Identity.Infrastructure;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public DbSet<Profile> Profiles => Set<Profile>();
    public DbSet<ExternalAccount> ExternalAccounts => Set<ExternalAccount>();
    public DbSet<Media.ObjectCleanup> ObjectCleanups => Set<Media.ObjectCleanup>();
    public DbSet<Media.OutboxEvent> Outbox => Set<Media.OutboxEvent>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("identity");
        modelBuilder.Entity<ExternalAccount>(entity =>
        {
            entity.ToTable("external_accounts");
            entity.HasKey(account => new { account.Issuer, account.Subject });
            entity.Property(account => account.Issuer).HasColumnName("issuer");
            entity.Property(account => account.Subject).HasColumnName("subject");
            entity.Property(account => account.ProfileId).HasColumnName("profile_id");
            entity.HasIndex(account => account.ProfileId).IsUnique();
            entity.HasOne<Profile>().WithOne().HasForeignKey<ExternalAccount>(account => account.ProfileId);
        });
        modelBuilder.Entity<Profile>(entity =>
        {
            entity.ToTable("profiles", table => table.HasCheckConstraint("ck_profiles_display_name", "length(btrim(display_name)) > 0"));
            entity.HasKey(profile => profile.Id);
            entity.Property(profile => profile.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(profile => profile.IsArtist).HasColumnName("is_artist").HasDefaultValue(false);
            entity.Property(profile => profile.DisplayName).HasColumnName("display_name").IsRequired();
            entity.Property(profile => profile.PhotoKey).HasColumnName("photo_key").HasMaxLength(200);
            entity.Property(profile => profile.PhotoPublic).HasColumnName("photo_public").HasDefaultValue(false);
            entity.Property(profile => profile.PhotoRevision).HasColumnName("photo_revision").HasDefaultValue(0);
        });
        modelBuilder.Entity<Media.ObjectCleanup>(entity =>
        {
            entity.ToTable("object_cleanups");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).ValueGeneratedNever();
            entity.Property(item => item.Key).HasMaxLength(512).IsRequired();
            entity.HasIndex(item => item.Key).IsUnique();
            entity.HasIndex(item => item.DueAt);
        });
        modelBuilder.Entity<Media.OutboxEvent>(entity =>
        {
            entity.ToTable("outbox");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).ValueGeneratedNever();
            entity.Property(item => item.Type).HasMaxLength(120).IsRequired();
            entity.Property(item => item.Payload).HasColumnType("jsonb").IsRequired();
            entity.Property(item => item.LastError).HasMaxLength(200);
            entity.HasIndex(item => new { item.PublishedAt, item.NextAttemptAt, item.CreatedAt });
        });
    }
}

internal sealed class EfProfileReader(IdentityDbContext database) : Application.IProfileReader
{
    public Task<Profile?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        database.Profiles.AsNoTracking().SingleOrDefaultAsync(profile => profile.Id == id, cancellationToken);
}
