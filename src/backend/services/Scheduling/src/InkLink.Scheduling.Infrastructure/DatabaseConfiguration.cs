using InkLink.Scheduling.Application.Reservations;
using InkLink.Scheduling.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Npgsql;

namespace InkLink.Scheduling.Infrastructure;

public sealed class SchedulingDatabaseOptions
{
    public const string Section = "SchedulingDatabase";
    public string ConnectionString { get; set; } = string.Empty;
}

public sealed class DatabaseOptionsValidator : IValidateOptions<SchedulingDatabaseOptions>
{
    public ValidateOptionsResult Validate(string? name, SchedulingDatabaseOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            return ValidateOptionsResult.Fail("SchedulingDatabase:ConnectionString is required. Configure environment variables or user secrets.");
        try
        {
            var connection = new NpgsqlConnectionStringBuilder(options.ConnectionString);
            if (string.IsNullOrWhiteSpace(connection.Host) || string.IsNullOrWhiteSpace(connection.Database)
                || string.IsNullOrWhiteSpace(connection.Username) || string.IsNullOrWhiteSpace(connection.Password))
                return ValidateOptionsResult.Fail("Scheduling database configuration requires host, database, username and password.");
            return ValidateOptionsResult.Success;
        }
        catch (ArgumentException)
        {
            return ValidateOptionsResult.Fail("Scheduling database configuration is invalid.");
        }
    }
}

public static class DependencyInjection
{
    public static IServiceCollection AddSchedulingInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<SchedulingDatabaseOptions>, DatabaseOptionsValidator>();
        services.AddOptions<SchedulingDatabaseOptions>().Bind(configuration.GetSection(SchedulingDatabaseOptions.Section)).ValidateOnStart();
        services.AddDbContext<SchedulingDbContext>((provider, options) => Configure(options,
            provider.GetRequiredService<IOptions<SchedulingDatabaseOptions>>().Value.ConnectionString));
        services.AddScoped<ISchedulingStore, EfSchedulingStore>();
        services.AddHealthChecks().AddCheck<SchedulingReadinessCheck>("scheduling-database", tags: ["ready"]);
        return services;
    }

    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString, postgres => postgres.MigrationsHistoryTable("__ef_migrations_history", "scheduling"));
}

public sealed class SchedulingDbContextFactory : IDesignTimeDbContextFactory<SchedulingDbContext>
{
    public SchedulingDbContext CreateDbContext(string[] args)
    {
        var configuration = new SchedulingDatabaseOptions
        {
            ConnectionString = Environment.GetEnvironmentVariable("SchedulingDatabase__ConnectionString") ?? string.Empty
        };
        var result = new DatabaseOptionsValidator().Validate(null, configuration);
        if (result.Failed) throw new InvalidOperationException(string.Join(" ", result.Failures));
        var options = new DbContextOptionsBuilder<SchedulingDbContext>();
        DependencyInjection.Configure(options, configuration.ConnectionString);
        return new SchedulingDbContext(options.Options);
    }
}

internal sealed class SchedulingReadinessCheck(SchedulingDbContext database) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await database.Reservations.Take(1).ToListAsync(cancellationToken);
            await database.Windows.Take(1).ToListAsync(cancellationToken);
            await database.FrozenEngagements.Take(1).ToListAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            return HealthCheckResult.Unhealthy("Scheduling database unavailable or schema not migrated.");
        }
    }
}
