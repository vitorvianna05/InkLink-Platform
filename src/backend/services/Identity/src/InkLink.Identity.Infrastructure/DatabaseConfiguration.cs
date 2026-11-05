using InkLink.ObjectStorage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Npgsql;

namespace InkLink.Identity.Infrastructure;

public sealed class IdentityDatabaseOptions
{
    public const string Section = "IdentityDatabase";
    public string ConnectionString { get; set; } = string.Empty;
}

public sealed class DatabaseOptionsValidator : IValidateOptions<IdentityDatabaseOptions>
{
    public ValidateOptionsResult Validate(string? name, IdentityDatabaseOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            return ValidateOptionsResult.Fail("IdentityDatabase:ConnectionString is required. Configure environment variables or user secrets.");
        try
        {
            var connection = new NpgsqlConnectionStringBuilder(options.ConnectionString);
            if (string.IsNullOrWhiteSpace(connection.Host) || string.IsNullOrWhiteSpace(connection.Database)
                || string.IsNullOrWhiteSpace(connection.Username) || string.IsNullOrWhiteSpace(connection.Password))
                return ValidateOptionsResult.Fail("Identity database configuration requires host, database, username and password.");
            return ValidateOptionsResult.Success;
        }
        catch (ArgumentException)
        {
            return ValidateOptionsResult.Fail("Identity database configuration is invalid.");
        }
    }
}

public static class DependencyInjection
{
    /// <summary>
    /// Fotos de perfil, limpeza de objetos e publicação de eventos (decisão 0018); usado somente pela API.
    /// Em testes isolados sem armazenamento ou broker configurado, a foto fica indisponível e o Outbox não é publicado.
    /// </summary>
    public static IServiceCollection AddIdentityMedia(this IServiceCollection services, IConfiguration configuration, bool localEnvironment, bool testing)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<Application.IPhotoStore, Media.EfPhotoStore>();
        var storage = configuration.GetSection("ObjectStorage");
        if (testing && string.IsNullOrWhiteSpace(storage["ServiceUrl"]))
        {
            services.AddSingleton<Application.IProfilePhotos, Media.DisabledProfilePhotos>();
        }
        else
        {
            services.AddObjectStorage(storage, localEnvironment);
            services.AddScoped<Application.IProfilePhotos, Media.ProfilePhotos>();
            services.AddSingleton<Media.ObjectCleanupWorker>();
            services.AddHostedService(provider => provider.GetRequiredService<Media.ObjectCleanupWorker>());
            services.AddHealthChecks().AddObjectStorageCheck();
        }
        services.AddSingleton<Messaging.OutboxWorker>();
        if (!(testing && string.IsNullOrWhiteSpace(configuration["Broker:Uri"])))
            services.AddHostedService(provider => provider.GetRequiredService<Messaging.OutboxWorker>());
        return services;
    }

    public static IServiceCollection AddIdentityInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<IdentityDatabaseOptions>, DatabaseOptionsValidator>();
        services.AddOptions<IdentityDatabaseOptions>().Bind(configuration.GetSection(IdentityDatabaseOptions.Section)).ValidateOnStart();
        services.AddDbContext<IdentityDbContext>((provider, options) => Configure(options,
            provider.GetRequiredService<IOptions<IdentityDatabaseOptions>>().Value.ConnectionString));
        services.AddScoped<Application.IProfileReader, EfProfileReader>();
        services.AddScoped<Application.IAccountStore, EfAccountStore>();
        services.AddHealthChecks().AddCheck<IdentityReadinessCheck>("identity-database", tags: ["ready"]);
        return services;
    }

    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString, postgres => postgres.MigrationsHistoryTable("__ef_migrations_history", "identity"));
}

public sealed class IdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args)
    {
        var configuration = new IdentityDatabaseOptions
        {
            ConnectionString = Environment.GetEnvironmentVariable("IdentityDatabase__ConnectionString") ?? string.Empty
        };
        var result = new DatabaseOptionsValidator().Validate(null, configuration);
        if (result.Failed) throw new InvalidOperationException(string.Join(" ", result.Failures));
        var options = new DbContextOptionsBuilder<IdentityDbContext>();
        DependencyInjection.Configure(options, configuration.ConnectionString);
        return new IdentityDbContext(options.Options);
    }
}

internal sealed class IdentityReadinessCheck(IdentityDbContext database) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await database.Profiles.Select(profile => new { profile.Id, profile.IsArtist }).Take(1).ToListAsync(cancellationToken);
            await database.ExternalAccounts.Select(account => account.ProfileId).Take(1).ToListAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            return HealthCheckResult.Unhealthy("Identity database unavailable or schema not migrated.");
        }
    }
}
