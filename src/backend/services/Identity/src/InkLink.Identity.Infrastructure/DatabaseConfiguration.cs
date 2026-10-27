using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
    public static IServiceCollection AddIdentityInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<IdentityDatabaseOptions>, DatabaseOptionsValidator>();
        services.AddOptions<IdentityDatabaseOptions>().Bind(configuration.GetSection(IdentityDatabaseOptions.Section)).ValidateOnStart();
        services.AddDbContext<IdentityDbContext>((provider, options) => Configure(options,
            provider.GetRequiredService<IOptions<IdentityDatabaseOptions>>().Value.ConnectionString));
        services.AddScoped<Application.IProfileReader, EfProfileReader>();
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
