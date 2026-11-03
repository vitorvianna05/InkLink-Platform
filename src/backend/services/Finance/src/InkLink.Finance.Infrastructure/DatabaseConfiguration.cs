using InkLink.Finance.Application.Ledger;
using InkLink.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Npgsql;

namespace InkLink.Finance.Infrastructure;

public sealed class FinanceDatabaseOptions
{
    public const string Section = "FinanceDatabase";
    public string ConnectionString { get; set; } = string.Empty;
}

public sealed class DatabaseOptionsValidator : IValidateOptions<FinanceDatabaseOptions>
{
    public ValidateOptionsResult Validate(string? name, FinanceDatabaseOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            return ValidateOptionsResult.Fail("FinanceDatabase:ConnectionString is required. Configure environment variables or user secrets.");
        try
        {
            var connection = new NpgsqlConnectionStringBuilder(options.ConnectionString);
            if (string.IsNullOrWhiteSpace(connection.Host) || string.IsNullOrWhiteSpace(connection.Database)
                || string.IsNullOrWhiteSpace(connection.Username) || string.IsNullOrWhiteSpace(connection.Password))
                return ValidateOptionsResult.Fail("Finance database configuration requires host, database, username and password.");
            return ValidateOptionsResult.Success;
        }
        catch (ArgumentException)
        {
            return ValidateOptionsResult.Fail("Finance database configuration is invalid.");
        }
    }
}

public static class DependencyInjection
{
    public static IServiceCollection AddFinanceInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<FinanceDatabaseOptions>, DatabaseOptionsValidator>();
        services.AddOptions<FinanceDatabaseOptions>().Bind(configuration.GetSection(FinanceDatabaseOptions.Section)).ValidateOnStart();
        services.AddDbContext<FinanceDbContext>((provider, options) => Configure(options,
            provider.GetRequiredService<IOptions<FinanceDatabaseOptions>>().Value.ConnectionString));
        services.AddScoped<ILedgerStore, EfLedgerStore>();
        services.AddHealthChecks().AddCheck<FinanceReadinessCheck>("finance-database", tags: ["ready"]);
        return services;
    }

    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString, postgres => postgres.MigrationsHistoryTable("__ef_migrations_history", "finance"));
}

public sealed class FinanceDbContextFactory : IDesignTimeDbContextFactory<FinanceDbContext>
{
    public FinanceDbContext CreateDbContext(string[] args)
    {
        var configuration = new FinanceDatabaseOptions
        {
            ConnectionString = Environment.GetEnvironmentVariable("FinanceDatabase__ConnectionString") ?? string.Empty
        };
        var result = new DatabaseOptionsValidator().Validate(null, configuration);
        if (result.Failed) throw new InvalidOperationException(string.Join(" ", result.Failures));
        var options = new DbContextOptionsBuilder<FinanceDbContext>();
        DependencyInjection.Configure(options, configuration.ConnectionString);
        return new FinanceDbContext(options.Options);
    }
}

internal sealed class FinanceReadinessCheck(FinanceDbContext database) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await database.Entries.Take(1).ToListAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            return HealthCheckResult.Unhealthy("Finance database unavailable or schema not migrated.");
        }
    }
}
