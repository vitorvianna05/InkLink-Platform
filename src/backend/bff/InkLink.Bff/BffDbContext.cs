using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace InkLink.Bff;

public sealed class BffDbContext(DbContextOptions<BffDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
    public DbSet<BffSession> Sessions => Set<BffSession>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("bff");
        modelBuilder.Entity<BffSession>().HasKey(session => session.Id);
        modelBuilder.Entity<BffSession>().HasIndex(session => session.ExpiresAt);
    }
    public static void Configure(DbContextOptionsBuilder options, string connection) =>
        options.UseNpgsql(connection, postgres => postgres.MigrationsHistoryTable("__ef_migrations_history", "bff"));
}
public sealed class BffSession
{
    public string Id { get; set; } = string.Empty;
    public byte[] ProtectedTicket { get; set; } = [];
    public DateTimeOffset ExpiresAt { get; set; }
}
public sealed class BffDbContextFactory : IDesignTimeDbContextFactory<BffDbContext>
{
    public BffDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("Bff__ConnectionString");
        if (string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException("Bff database configuration is required.");
        var options = new DbContextOptionsBuilder<BffDbContext>();
        BffDbContext.Configure(options, connection);
        return new(options.Options);
    }
}
