using InkLink.Finance.Domain;
using Microsoft.EntityFrameworkCore;
namespace InkLink.Finance.Infrastructure.Persistence;

public sealed class FinanceDbContext(DbContextOptions<FinanceDbContext> options) : DbContext(options)
{
    public DbSet<LedgerEntry> Entries => Set<LedgerEntry>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("finance");
        model.Entity<LedgerEntry>(e =>
        {
            e.ToTable("entries", t => t.HasCheckConstraint("ck_positive_amount", "\"Amount\" > 0 AND \"Amount\" <= 9999999.99"));
            e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Description).HasMaxLength(4000);
            e.Property(x => x.Amount).HasPrecision(9, 2);
            e.Property(x => x.Fingerprint).HasMaxLength(64);
            e.Property(x => x.TimeZoneId).HasMaxLength(100);
            e.HasIndex(x => new { x.ArtistId, x.RequestKey }).IsUnique();
            e.HasIndex(x => x.OriginalEntryId).IsUnique();
            e.HasIndex(x => new { x.ArtistId, x.EffectiveDate, x.Id });
            e.HasOne<LedgerEntry>().WithMany().HasForeignKey(x => x.OriginalEntryId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
