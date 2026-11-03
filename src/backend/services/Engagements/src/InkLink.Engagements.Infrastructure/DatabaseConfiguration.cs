using InkLink.Engagements.Application.Negotiations;
using InkLink.Engagements.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Npgsql;

namespace InkLink.Engagements.Infrastructure;

public sealed class EngagementsDatabaseOptions
{
    public const string Section = "EngagementsDatabase";
    public string ConnectionString { get; set; } = string.Empty;
}

public sealed class DatabaseOptionsValidator : IValidateOptions<EngagementsDatabaseOptions>
{
    public ValidateOptionsResult Validate(string? name, EngagementsDatabaseOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            return ValidateOptionsResult.Fail("EngagementsDatabase:ConnectionString is required. Configure environment variables or user secrets.");
        try
        {
            var connection = new NpgsqlConnectionStringBuilder(options.ConnectionString);
            if (string.IsNullOrWhiteSpace(connection.Host) || string.IsNullOrWhiteSpace(connection.Database)
                || string.IsNullOrWhiteSpace(connection.Username) || string.IsNullOrWhiteSpace(connection.Password))
                return ValidateOptionsResult.Fail("Engagements database configuration requires host, database, username and password.");
            return ValidateOptionsResult.Success;
        }
        catch (ArgumentException)
        {
            return ValidateOptionsResult.Fail("Engagements database configuration is invalid.");
        }
    }
}

public static class DependencyInjection
{
    public static IServiceCollection AddEngagementsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<EngagementsDatabaseOptions>, DatabaseOptionsValidator>();
        services.AddOptions<EngagementsDatabaseOptions>().Bind(configuration.GetSection(EngagementsDatabaseOptions.Section)).ValidateOnStart();
        services.AddDbContext<EngagementsDbContext>((provider, options) => Configure(options,
            provider.GetRequiredService<IOptions<EngagementsDatabaseOptions>>().Value.ConnectionString));
        services.AddScoped<IEngagementStore, EfEngagementStore>();
        services.AddHealthChecks().AddCheck<EngagementsReadinessCheck>("engagements-database", tags: ["ready"]);
        return services;
    }

    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString, postgres => postgres.MigrationsHistoryTable("__ef_migrations_history", "engagements"));
}

public sealed class EngagementsDbContextFactory : IDesignTimeDbContextFactory<EngagementsDbContext>
{
    public EngagementsDbContext CreateDbContext(string[] args)
    {
        var configuration = new EngagementsDatabaseOptions
        {
            ConnectionString = Environment.GetEnvironmentVariable("EngagementsDatabase__ConnectionString") ?? string.Empty
        };
        var result = new DatabaseOptionsValidator().Validate(null, configuration);
        if (result.Failed) throw new InvalidOperationException(string.Join(" ", result.Failures));
        var options = new DbContextOptionsBuilder<EngagementsDbContext>();
        DependencyInjection.Configure(options, configuration.ConnectionString);
        return new EngagementsDbContext(options.Options);
    }
}

internal sealed class EngagementsReadinessCheck(EngagementsDbContext database) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await database.Engagements.Take(1).ToListAsync(cancellationToken);
            await database.Proposals.Take(1).ToListAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            return HealthCheckResult.Unhealthy("Engagements database unavailable or schema not migrated.");
        }
    }
}
