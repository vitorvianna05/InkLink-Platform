using InkLink.Messaging.Application.Conversations;
using InkLink.Messaging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Npgsql;

namespace InkLink.Messaging.Infrastructure;

public sealed class MessagingDatabaseOptions
{
    public const string Section = "MessagingDatabase";
    public string ConnectionString { get; set; } = string.Empty;
}

public sealed class DatabaseOptionsValidator : IValidateOptions<MessagingDatabaseOptions>
{
    public ValidateOptionsResult Validate(string? name, MessagingDatabaseOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            return ValidateOptionsResult.Fail("MessagingDatabase:ConnectionString is required. Configure environment variables or user secrets.");
        try
        {
            var connection = new NpgsqlConnectionStringBuilder(options.ConnectionString);
            if (string.IsNullOrWhiteSpace(connection.Host) || string.IsNullOrWhiteSpace(connection.Database)
                || string.IsNullOrWhiteSpace(connection.Username) || string.IsNullOrWhiteSpace(connection.Password))
                return ValidateOptionsResult.Fail("Messaging database configuration requires host, database, username and password.");
            return ValidateOptionsResult.Success;
        }
        catch (ArgumentException)
        {
            return ValidateOptionsResult.Fail("Messaging database configuration is invalid.");
        }
    }
}

public static class DependencyInjection
{
    public static IServiceCollection AddMessagingInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<MessagingDatabaseOptions>, DatabaseOptionsValidator>();
        services.AddOptions<MessagingDatabaseOptions>().Bind(configuration.GetSection(MessagingDatabaseOptions.Section)).ValidateOnStart();
        services.AddDbContext<MessagingDbContext>((provider, options) => Configure(options,
            provider.GetRequiredService<IOptions<MessagingDatabaseOptions>>().Value.ConnectionString));
        services.AddScoped<IConversationStore, EfConversationStore>();
        services.AddHealthChecks().AddCheck<MessagingReadinessCheck>("messaging-database", tags: ["ready"]);
        return services;
    }

    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString, postgres => postgres.MigrationsHistoryTable("__ef_migrations_history", "messaging"));
}

public sealed class MessagingDbContextFactory : IDesignTimeDbContextFactory<MessagingDbContext>
{
    public MessagingDbContext CreateDbContext(string[] args)
    {
        var configuration = new MessagingDatabaseOptions
        {
            ConnectionString = Environment.GetEnvironmentVariable("MessagingDatabase__ConnectionString") ?? string.Empty
        };
        var result = new DatabaseOptionsValidator().Validate(null, configuration);
        if (result.Failed) throw new InvalidOperationException(string.Join(" ", result.Failures));
        var options = new DbContextOptionsBuilder<MessagingDbContext>();
        DependencyInjection.Configure(options, configuration.ConnectionString);
        return new MessagingDbContext(options.Options);
    }
}

internal sealed class MessagingReadinessCheck(MessagingDbContext database) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await database.Conversations.Take(1).ToListAsync(cancellationToken);
            await database.Messages.Take(1).ToListAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            return HealthCheckResult.Unhealthy("Messaging database unavailable or schema not migrated.");
        }
    }
}
