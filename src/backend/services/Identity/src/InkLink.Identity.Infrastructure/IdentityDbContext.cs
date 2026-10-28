using InkLink.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace InkLink.Identity.Infrastructure;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public DbSet<Profile> Profiles => Set<Profile>();
    public DbSet<ExternalAccount> ExternalAccounts => Set<ExternalAccount>();
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
        });
    }
}

internal sealed class EfProfileReader(IdentityDbContext database) : Application.IProfileReader
{
    public Task<Profile?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        database.Profiles.AsNoTracking().SingleOrDefaultAsync(profile => profile.Id == id, cancellationToken);
}
