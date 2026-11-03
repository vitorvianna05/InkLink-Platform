using System.Net.Http.Json;
using System.Text.Json.Nodes;
using InkLink.Catalog.Infrastructure;
using InkLink.Catalog.Infrastructure.Persistence;
using InkLink.Identity.Application;
using InkLink.Identity.IntegrationTests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Npgsql;
using Testcontainers.PostgreSql;

namespace InkLink.Catalog.IntegrationTests;

public sealed class CatalogFixture : IAsyncLifetime
{
    public SecurityFixture Security { get; } = new();
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17.7-alpine").Build();
    internal SecurityIdentityHost Identity { get; private set; } = null!;
    public Dictionary<string, string> Tokens { get; } = [];

    public async Task InitializeAsync()
    {
        await Security.InitializeAsync();
        await postgres.StartAsync();
        await using (var admin = new NpgsqlConnection(postgres.GetConnectionString()))
        {
            await admin.OpenAsync();
            await Execute(admin, "CREATE DATABASE catalog_other_context");
            await Execute(admin, "REVOKE CONNECT ON DATABASE catalog_other_context FROM PUBLIC");
        }
        Identity = new SecurityIdentityHost(Security);
        using var http = Identity.CreateClient();
        foreach (var name in new[] { "alice", "bob", "unprovisioned" })
        {
            var token = (await Security.TokenAsync(name))["access_token"]!.GetValue<string>();
            Tokens[name] = token;
            http.DefaultRequestHeaders.Authorization = new("Bearer", token);
            Assert.Equal(System.Net.HttpStatusCode.OK, (await http.GetAsync("/api/v1/profiles/me")).StatusCode);
            if (name != "unprovisioned")
            {
                using var scope = Identity.Services.CreateScope();
                var activate = new ActivateArtist(scope.ServiceProvider.GetRequiredService<IAccountStore>());
                Assert.True(await activate.ExecuteAsync(Security.Authority, new JsonWebToken(token).Subject, default));
            }
        }
    }

    public async Task<CatalogScenario> CreateAsync(bool migrate = true)
    {
        foreach (var name in Tokens.Keys.ToArray()) Tokens[name] = (await Security.TokenAsync(name))["access_token"]!.GetValue<string>();
        var suffix = Guid.NewGuid().ToString("N");
        var dbName = "catalog_" + suffix;
        var ownerName = "owner_" + suffix;
        var appName = "app_" + suffix;
        var password = Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(postgres.GetConnectionString());
        await admin.OpenAsync();
        await Execute(admin, $"CREATE ROLE {ownerName} LOGIN PASSWORD '{password}'; CREATE ROLE {appName} LOGIN PASSWORD '{password}'");
        await Execute(admin, $"CREATE DATABASE {dbName} OWNER {ownerName}");
        await Execute(admin, $"REVOKE CONNECT ON DATABASE {dbName} FROM PUBLIC; GRANT CONNECT ON DATABASE {dbName} TO {ownerName}, {appName}");
        var owner = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString()) { Database = dbName, Username = ownerName, Password = password }.ConnectionString;
        var app = new NpgsqlConnectionStringBuilder(owner) { Username = appName }.ConnectionString;
        await using (var connection = new NpgsqlConnection(owner))
        {
            await connection.OpenAsync();
            await Execute(connection, $"REVOKE CREATE ON SCHEMA public FROM PUBLIC; CREATE SCHEMA catalog AUTHORIZATION {ownerName}; GRANT USAGE ON SCHEMA catalog TO {appName}; ALTER DEFAULT PRIVILEGES IN SCHEMA catalog GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO {appName}");
        }
        if (migrate)
        {
            var options = new DbContextOptionsBuilder<CatalogDbContext>();
            DependencyInjection.Configure(options, owner);
            await using var database = new CatalogDbContext(options.Options);
            await database.Database.MigrateAsync();
            await database.Database.MigrateAsync();
        }
        return new CatalogScenario(this, owner, app);
    }

    private static async Task Execute(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        Identity?.Dispose();
        await postgres.DisposeAsync();
        await Security.DisposeAsync();
    }
}

[CollectionDefinition("catalog")]
public sealed class CatalogCollection : ICollectionFixture<CatalogFixture>;

public sealed class CatalogScenario : IDisposable
{
    internal CatalogHost Host { get; }
    internal SecurityGatewayHost Gateway { get; }
    public HttpClient Http { get; }
    public HttpClient Proxied { get; }
    public string OwnerConnection { get; }
    public string AppConnection { get; }
    public CatalogFixture Fixture { get; }

    internal CatalogScenario(CatalogFixture fixture, string owner, string app)
    {
        Fixture = fixture; OwnerConnection = owner; AppConnection = app;
        Host = new CatalogHost(fixture, app);
        Http = Host.CreateClient();
        Gateway = new SecurityGatewayHost(fixture.Security, Host.Server.CreateHandler());
        Proxied = Gateway.CreateClient();
    }

    public void SignIn(string user, bool throughGateway = false) => (throughGateway ? Proxied : Http).DefaultRequestHeaders.Authorization = new("Bearer", Fixture.Tokens[user]);

    public async Task<Guid> PublishAsync(string user = "alice", string name = "Rafael Moura", string city = "São Paulo", decimal price = 300, string[]? styles = null, string studio = "Estúdio Central")
    {
        SignIn(user);
        var response = await Http.PutAsJsonAsync("/api/v1/artists/me", new { name, studio, city, biography = "Trabalhos autorais.", styles = styles ?? ["Blackwork"], startingPrice = price });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var id = (await response.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<Guid>();
        return id;
    }

    public void Dispose() { Proxied.Dispose(); Gateway.Dispose(); Http.Dispose(); Host.Dispose(); }
}
