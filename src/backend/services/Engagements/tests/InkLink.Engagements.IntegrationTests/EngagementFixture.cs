using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using InkLink.Engagements.Application.Accounts;
using InkLink.Engagements.Application.Negotiations;
using InkLink.Engagements.Infrastructure.Integration;
using InkLink.Engagements.Infrastructure.Persistence;
using InkLink.Identity.Application;
using InkLink.Identity.IntegrationTests;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Testcontainers.PostgreSql;
using Yarp.ReverseProxy.Forwarder;

namespace InkLink.Engagements.IntegrationTests;

public sealed class EngagementFixture : IAsyncLifetime
{
    public const string Issuer = "https://issuer.p04.test/realms/inklink";
    private readonly RSA rsa = RSA.Create(2048);
    private PostgreSqlContainer? container;
    private string adminConnection = string.Empty;
    private readonly List<(string Database, string Owner, string App)> resources = [];
    private string identityConnection = string.Empty;
    private string catalogConnection = string.Empty;
    public string OwnerConnection { get; private set; } = string.Empty;
    public string AppConnection { get; private set; } = string.Empty;
    public string CatalogDatabaseName => new NpgsqlConnectionStringBuilder(catalogConnection).Database!;
    private readonly string localServiceSecret = Guid.NewGuid().ToString("N");
    public string ServiceSecret => RealProvider?.EngagementsSecret ?? localServiceSecret;
    public SecurityFixture? RealProvider { get; private set; }
    public string Authority => RealProvider?.Authority ?? Issuer;
    public string UserClient => RealProvider is null ? "p04-users" : "inklink-test-users";
    private readonly Dictionary<string, string> realTokens = [];
    public void UseRealIdentity(SecurityFixture provider) => RealProvider = provider;
    public Dictionary<string, Guid> Accounts { get; } = [];
    public ServiceHost<IdentityProgram> Identity { get; private set; } = null!;
    public ServiceHost<CatalogProgram> Catalog { get; private set; } = null!;
    public ServiceHost<EngagementsProgram> Engagements { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        if (RealProvider is not null)
            foreach (var user in new[] { "alice", "bob", "carol" }) realTokens[user] = (await RealProvider.TokenAsync(user))["access_token"]!.GetValue<string>();
        adminConnection = Environment.GetEnvironmentVariable("INKLINK_TEST_POSTGRES") ?? string.Empty;
        if (adminConnection.Length == 0)
        {
            container = new PostgreSqlBuilder("postgres:17.7-alpine").Build();
            await container.StartAsync();
            adminConnection = container.GetConnectionString();
        }
        var identity = await DatabaseAsync("identity"); identityConnection = identity.App;
        var catalog = await DatabaseAsync("catalog"); catalogConnection = catalog.App;
        var engagements = await DatabaseAsync("engagements"); OwnerConnection = engagements.Owner; AppConnection = engagements.App;
        var identityOptions = new DbContextOptionsBuilder<InkLink.Identity.Infrastructure.IdentityDbContext>();
        InkLink.Identity.Infrastructure.DependencyInjection.Configure(identityOptions, identity.Owner);
        await using (var database = new InkLink.Identity.Infrastructure.IdentityDbContext(identityOptions.Options)) await database.Database.MigrateAsync();
        var catalogOptions = new DbContextOptionsBuilder<InkLink.Catalog.Infrastructure.Persistence.CatalogDbContext>();
        InkLink.Catalog.Infrastructure.DependencyInjection.Configure(catalogOptions, catalog.Owner);
        await using (var database = new InkLink.Catalog.Infrastructure.Persistence.CatalogDbContext(catalogOptions.Options)) await database.Database.MigrateAsync();
        await using (var database = CreateOwnerContext())
        {
            await database.Database.MigrateAsync();
            await database.Database.MigrateAsync();
        }
        Identity = new(this, "identity");
        using var http = Identity.CreateClient();
        foreach (var user in new[] { "alice", "bob", "carol" })
        {
            http.DefaultRequestHeaders.Authorization = new("Bearer", Token(user));
            var response = await http.GetFromJsonAsync<JsonObject>("/api/v1/profiles/me");
            Accounts[user] = response!["id"]!.GetValue<Guid>();
            if (user != "carol")
            {
                using var scope = Identity.Services.CreateScope();
                Assert.True(await new ActivateArtist(scope.ServiceProvider.GetRequiredService<IAccountStore>()).ExecuteAsync(Authority, new JsonWebToken(Token(user)).Subject, default));
            }
        }
        Catalog = new(this, "catalog");
        Engagements = NewHost();
    }

    public ServiceHost<EngagementsProgram> NewHost(bool identityUnavailable = false, bool catalogUnavailable = false) => new(this, "engagements", identityUnavailable, catalogUnavailable);
    public ServiceHost<GatewayProgram> Gateway() => new(this, "gateway");

    public string Token(string user = "alice", string actor = "user", string client = "p04-users", string scope = "", bool expired = false)
    {
        if (RealProvider is not null) return realTokens[user];
        var key = new RsaSecurityKey(rsa) { KeyId = "p04" };
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = "inklink-api",
            IssuedAt = DateTime.UtcNow.AddMinutes(-2),
            NotBefore = DateTime.UtcNow.AddMinutes(-2),
            Expires = expired ? DateTime.UtcNow.AddMinutes(-1) : DateTime.UtcNow.AddMinutes(20),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256),
            Claims = new Dictionary<string, object> { ["sub"] = user, ["name"] = user, ["typ"] = "Bearer", ["inklink_actor"] = actor, ["azp"] = client, ["scope"] = scope }
        });
    }

    public void ValidateTokens(JwtBearerOptions options)
    {
        if (RealProvider is not null) { options.BackchannelHttpHandler = RealProvider.CreateProviderHandler(); return; }
        options.Configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
        options.Configuration.SigningKeys.Add(new RsaSecurityKey(rsa) { KeyId = "p04" });
        options.TokenValidationParameters.IssuerSigningKey = new RsaSecurityKey(rsa) { KeyId = "p04" };
    }

    internal Dictionary<string, string?> Settings(string kind) => new()
    {
        ["Authentication:Authority"] = Authority,
        ["Authentication:Audience"] = "inklink-api",
        ["Authentication:UserClients:0"] = UserClient,
        ["Authentication:TechnicalClients:0"] = "inklink-catalog",
        ["Authentication:TechnicalClients:1"] = "inklink-messaging",
        ["Authentication:TechnicalClients:2"] = "inklink-scheduling",
        ["IdentityDatabase:ConnectionString"] = identityConnection,
        ["CatalogDatabase:ConnectionString"] = catalogConnection,
        ["EngagementsDatabase:ConnectionString"] = AppConnection,
        ["Catalog:IdentityAddress"] = "http://identity.test/",
        ["Engagements:IdentityAddress"] = "http://identity.test/",
        ["Engagements:CatalogAddress"] = "http://catalog.test/",
        ["Engagements:ClientSecret"] = ServiceSecret,
        ["Gateway:IdentityAddress"] = "http://identity.test/",
        ["Gateway:CatalogAddress"] = "http://catalog.test/",
        ["Gateway:EngagementsAddress"] = "http://engagements.test/"
    };

    public EngagementsDbContext CreateOwnerContext()
    {
        var options = new DbContextOptionsBuilder<EngagementsDbContext>();
        Infrastructure.DependencyInjection.Configure(options, OwnerConnection);
        return new(options.Options);
    }

    public async Task<(string Owner, string App)> DatabaseAsync(string schema)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var name = "p04_" + suffix; var owner = "own_" + suffix; var app = "app_" + suffix; var password = Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(adminConnection); await admin.OpenAsync();
        await Execute(admin, $"CREATE ROLE {owner} LOGIN PASSWORD '{password}'; CREATE ROLE {app} LOGIN PASSWORD '{password}'");
        await Execute(admin, $"CREATE DATABASE {name} OWNER {owner}");
        await Execute(admin, $"REVOKE CONNECT ON DATABASE {name} FROM PUBLIC; GRANT CONNECT ON DATABASE {name} TO {owner}, {app}");
        var ownerConnection = new NpgsqlConnectionStringBuilder(adminConnection) { Database = name, Username = owner, Password = password }.ConnectionString;
        var appConnection = new NpgsqlConnectionStringBuilder(ownerConnection) { Username = app }.ConnectionString;
        await using var connection = new NpgsqlConnection(ownerConnection); await connection.OpenAsync();
        await Execute(connection, $"REVOKE CREATE ON SCHEMA public FROM PUBLIC; CREATE SCHEMA {schema} AUTHORIZATION {owner}; GRANT USAGE ON SCHEMA {schema} TO {app}; ALTER DEFAULT PRIVILEGES IN SCHEMA {schema} GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO {app}");
        resources.Add((name, owner, app));
        return (ownerConnection, appConnection);
    }

    private static async Task Execute(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection); await command.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        Engagements?.Dispose(); Catalog?.Dispose(); Identity?.Dispose(); NpgsqlConnection.ClearAllPools();
        if (container is not null) await container.DisposeAsync();
        else if (adminConnection.Length > 0)
        {
            await using var admin = new NpgsqlConnection(adminConnection); await admin.OpenAsync();
            foreach (var resource in resources)
            {
                await Execute(admin, $"DROP DATABASE {resource.Database} WITH (FORCE)");
                await Execute(admin, $"DROP ROLE {resource.Owner}; DROP ROLE {resource.App}");
            }
        }
        rsa.Dispose();
    }
}

[CollectionDefinition("engagements")]
public sealed class EngagementCollection : ICollectionFixture<EngagementFixture>;

public sealed class ServiceHost<T>(EngagementFixture fixture, string kind, bool identityUnavailable = false, bool catalogUnavailable = false) : WebApplicationFactory<T> where T : class
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        foreach (var setting in fixture.Settings(kind)) builder.UseSetting(setting.Key, setting.Value);
        builder.ConfigureTestServices(services =>
        {
            services.Configure<JwtBearerOptions>("Bearer", fixture.ValidateTokens);
            if (kind == "catalog") services.AddHttpClient<InkLink.Catalog.Application.Accounts.IIdentityDirectory, InkLink.Catalog.Infrastructure.Integration.IdentityDirectory>()
                .ConfigurePrimaryHttpMessageHandler(() => fixture.Identity.Server.CreateHandler());
            if (kind == "engagements")
            {
                services.AddHttpClient<IIdentityDirectory, IdentityDirectory>().ConfigurePrimaryHttpMessageHandler(() => identityUnavailable ? new FailureHandler() : fixture.Identity.Server.CreateHandler());
                services.AddHttpClient<ICatalogDirectory, CatalogDirectory>().ConfigurePrimaryHttpMessageHandler(() => catalogUnavailable ? new FailureHandler() : fixture.Catalog.Server.CreateHandler());
                services.AddHttpClient<ServiceTokenClient>().ConfigurePrimaryHttpMessageHandler(() => fixture.RealProvider is null ? new TokenHandler(fixture) : fixture.RealProvider.CreateProviderHandler());
            }
            if (kind == "gateway") services.AddSingleton<IForwarderHttpClientFactory>(new ProxyClient(fixture.Engagements.Server.CreateHandler()));
        });
    }
}

internal sealed class FailureHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
}

internal sealed class TokenHandler(EngagementFixture fixture) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var form = await request.Content!.ReadAsStringAsync(cancellationToken);
        Assert.Contains("client_id=inklink-engagements", form); Assert.Contains("client_secret=" + fixture.ServiceSecret, form);
        Assert.Contains("scope=catalog.engagement-context.read", form); Assert.Contains("grant_type=client_credentials", form);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { access_token = fixture.Token("service-account", "service", "inklink-engagements", "catalog.engagement-context.read") }) };
    }
}

internal sealed class ProxyClient(HttpMessageHandler handler) : IForwarderHttpClientFactory
{
    public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) => new(handler, disposeHandler: false);
}
