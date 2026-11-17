using InkLink.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace InkLink.Identity.Infrastructure;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public DbSet<Profile> Profiles => Set<Profile>();
    public DbSet<ExternalAccount> ExternalAccounts => Set<ExternalAccount>();
    public DbSet<Media.ObjectCleanup> ObjectCleanups => Set<Media.ObjectCleanup>();
    public DbSet<Media.OutboxEvent> Outbox => Set<Media.OutboxEvent>();
    public DbSet<VerificationCode> VerificationCodes => Set<VerificationCode>();
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
            // Cadastro pelo próprio usuário (decisão 0020). Contas existentes ficam ativas (padrão falso).
            entity.Property(profile => profile.RegistrationPending).HasColumnName("registration_pending").HasDefaultValue(false);
            entity.Property(profile => profile.TermsVersion).HasColumnName("terms_version").HasMaxLength(60);
            entity.Property(profile => profile.TermsAcceptedAt).HasColumnName("terms_accepted_at");
            entity.Property(profile => profile.ConfirmedEmail).HasColumnName("confirmed_email").HasMaxLength(320);
            entity.Property(profile => profile.EmailConfirmedAt).HasColumnName("email_confirmed_at");
            entity.Property(profile => profile.PhoneNumber).HasColumnName("phone_number").HasMaxLength(16);
            entity.Property(profile => profile.PhoneConfirmedAt).HasColumnName("phone_confirmed_at");
            entity.Property(profile => profile.ArtistRequestedAt).HasColumnName("artist_requested_at");
            entity.Ignore(profile => profile.ArtistRequestStatus);
            // Um celular confirmado pertence a uma única conta.
            entity.HasIndex(profile => profile.PhoneNumber).IsUnique().HasFilter("phone_confirmed_at IS NOT NULL").HasDatabaseName("ix_profiles_confirmed_phone");
        });
        modelBuilder.Entity<VerificationCode>(entity =>
        {
            entity.ToTable("verification_codes", table => table.HasCheckConstraint("ck_verification_codes_attempts", "attempts >= 0"));
            entity.HasKey(code => code.Id);
            entity.Property(code => code.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(code => code.ProfileId).HasColumnName("profile_id");
            entity.Property(code => code.Channel).HasColumnName("channel").HasConversion<string>().HasMaxLength(10);
            entity.Property(code => code.Target).HasColumnName("target").HasMaxLength(320).IsRequired();
            entity.Property(code => code.CodeHash).HasColumnName("code_hash").HasMaxLength(64).IsRequired();
            entity.Property(code => code.CreatedAt).HasColumnName("created_at");
            entity.Property(code => code.ExpiresAt).HasColumnName("expires_at");
            entity.Property(code => code.Attempts).HasColumnName("attempts");
            entity.Property(code => code.ClosedAt).HasColumnName("closed_at");
            entity.HasIndex(code => new { code.ProfileId, code.Channel, code.CreatedAt });
            entity.HasOne<Profile>().WithMany().HasForeignKey(code => code.ProfileId).OnDelete(DeleteBehavior.Cascade);
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
