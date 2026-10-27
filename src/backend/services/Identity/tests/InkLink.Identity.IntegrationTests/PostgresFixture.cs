using InkLink.Identity.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace InkLink.Identity.IntegrationTests;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:17.7-alpine").Build();
    private readonly string ownerPassword = Guid.NewGuid().ToString("N");
    private readonly string appPassword = Guid.NewGuid().ToString("N");
    public string OwnerConnection { get; private set; } = string.Empty;
    public string AppConnection { get; private set; } = string.Empty;
    public string UnmigratedConnection { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await container.StartAsync();
        await using var admin = new NpgsqlConnection(container.GetConnectionString());
        await admin.OpenAsync();
        await Execute(admin, $"CREATE ROLE inklink_identity_owner LOGIN PASSWORD '{ownerPassword}'");
        await Execute(admin, $"CREATE ROLE inklink_identity_app LOGIN PASSWORD '{appPassword}'");
        await Execute(admin, "CREATE DATABASE inklink_identity OWNER inklink_identity_owner");
        await Execute(admin, "CREATE DATABASE other_context");
        await Execute(admin, "CREATE DATABASE identity_unmigrated OWNER inklink_identity_owner");
        await Execute(admin, "REVOKE CONNECT ON DATABASE identity_unmigrated FROM PUBLIC");
        await Execute(admin, "GRANT CONNECT ON DATABASE identity_unmigrated TO inklink_identity_app");
        await Execute(admin, "REVOKE CONNECT ON DATABASE other_context FROM PUBLIC");
        await Execute(admin, "REVOKE CONNECT ON DATABASE inklink_identity FROM PUBLIC");
        await Execute(admin, "GRANT CONNECT ON DATABASE inklink_identity TO inklink_identity_owner, inklink_identity_app");
        OwnerConnection = Connection("inklink_identity_owner", ownerPassword);
        AppConnection = Connection("inklink_identity_app", appPassword);
        UnmigratedConnection = new NpgsqlConnectionStringBuilder(AppConnection) { Database = "identity_unmigrated" }.ConnectionString;
        await using var owner = new NpgsqlConnection(OwnerConnection);
        await owner.OpenAsync();
        await Execute(owner, "CREATE SCHEMA identity AUTHORIZATION inklink_identity_owner");
        await Execute(owner, "GRANT USAGE ON SCHEMA identity TO inklink_identity_app");
        await Execute(owner, "ALTER DEFAULT PRIVILEGES IN SCHEMA identity GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO inklink_identity_app");
        await using var database = CreateOwnerContext();
        await database.Database.MigrateAsync();
    }

    public IdentityDbContext CreateOwnerContext()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>();
        DependencyInjection.Configure(options, OwnerConnection);
        return new(options.Options);
    }

    private string Connection(string user, string password) => new NpgsqlConnectionStringBuilder(container.GetConnectionString())
    {
        Database = "inklink_identity",
        Username = user,
        Password = password,
        IncludeErrorDetail = false
    }.ConnectionString;

    public static async Task Execute(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync() => await container.DisposeAsync();
}
