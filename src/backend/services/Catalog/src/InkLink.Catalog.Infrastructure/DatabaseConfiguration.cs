using InkLink.Catalog.Infrastructure.Persistence;
using InkLink.Catalog.Infrastructure.Queries;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Npgsql;

namespace InkLink.Catalog.Infrastructure;

public sealed class CatalogDatabaseOptions
{
    public const string Section = "CatalogDatabase";
    public string ConnectionString { get; set; } = string.Empty;
}

public sealed class DatabaseOptionsValidator : IValidateOptions<CatalogDatabaseOptions>
{
    public ValidateOptionsResult Validate(string? name, CatalogDatabaseOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            return ValidateOptionsResult.Fail("CatalogDatabase:ConnectionString is required. Configure environment variables or user secrets.");
        try
        {
            var connection = new NpgsqlConnectionStringBuilder(options.ConnectionString);
            if (string.IsNullOrWhiteSpace(connection.Host) || string.IsNullOrWhiteSpace(connection.Database)
                || string.IsNullOrWhiteSpace(connection.Username) || string.IsNullOrWhiteSpace(connection.Password))
                return ValidateOptionsResult.Fail("Catalog database configuration requires host, database, username and password.");
            return ValidateOptionsResult.Success;
        }
        catch (ArgumentException)
        {
            return ValidateOptionsResult.Fail("Catalog database configuration is invalid.");
        }
    }
}

public static class DependencyInjection
{
    public static IServiceCollection AddCatalogInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<CatalogDatabaseOptions>, DatabaseOptionsValidator>();
        services.AddOptions<CatalogDatabaseOptions>().Bind(configuration.GetSection(CatalogDatabaseOptions.Section)).ValidateOnStart();
        services.AddDbContext<CatalogDbContext>((provider, options) => Configure(options,
            provider.GetRequiredService<IOptions<CatalogDatabaseOptions>>().Value.ConnectionString));
        services.AddScoped<Application.Queries.ICatalogQueries, EfCatalogQueries>();
        services.AddScoped<Application.Queries.IEngagementContextReader, EfEngagementContextReader>();
        services.AddScoped<Application.Publishing.IArtistStore, EfArtistStore>();
        services.AddScoped<Application.Favorites.IFavoriteStore, EfFavoriteStore>();
        services.AddHealthChecks().AddCheck<CatalogReadinessCheck>("catalog-database", tags: ["ready"]);
        return services;
    }

    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString, postgres => postgres.MigrationsHistoryTable("__ef_migrations_history", "catalog"));
}

public sealed class CatalogDbContextFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        var configuration = new CatalogDatabaseOptions
        {
            ConnectionString = Environment.GetEnvironmentVariable("CatalogDatabase__ConnectionString") ?? string.Empty
        };
        var result = new DatabaseOptionsValidator().Validate(null, configuration);
        if (result.Failed) throw new InvalidOperationException(string.Join(" ", result.Failures));
        var options = new DbContextOptionsBuilder<CatalogDbContext>();
        DependencyInjection.Configure(options, configuration.ConnectionString);
        return new CatalogDbContext(options.Options);
    }
}

internal sealed class CatalogReadinessCheck(CatalogDbContext database) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await database.Artists.Select(artist => artist.Id).Take(1).ToListAsync(cancellationToken);
            await database.Artworks.Select(artwork => artwork.Id).Take(1).ToListAsync(cancellationToken);
            await database.SearchTerms.Take(1).ToListAsync(cancellationToken);
            await database.Favorites.Take(1).ToListAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            return HealthCheckResult.Unhealthy("Catalog database unavailable or schema not migrated.");
        }
    }
}
