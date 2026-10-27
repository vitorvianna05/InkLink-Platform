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
        return services;
    }
}
