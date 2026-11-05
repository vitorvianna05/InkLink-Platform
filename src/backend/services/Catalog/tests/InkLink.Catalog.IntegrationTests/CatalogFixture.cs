using System.Net.Http.Json;
using System.Text.Json.Nodes;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
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
    /// <summary>SeaweedFS real com as identidades e o script de buckets versionados (decisão 0018).</summary>
    public string StorageSecret { get; } = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
    private IContainer storage = null!;
    public Uri StorageEndpoint => new($"http://127.0.0.1:{storage.GetMappedPublicPort(8333)}/");
    internal SecurityIdentityHost Identity { get; private set; } = null!;
    public Dictionary<string, string> Tokens { get; } = [];

    public async Task InitializeAsync()
    {
        // O worker de limpeza é acionado explicitamente pelos testes.
        InkLink.Catalog.Infrastructure.Media.ObjectCleanupWorker.Interval = TimeSpan.FromHours(1);
        await Security.InitializeAsync();
        await postgres.StartAsync();
        var identities = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "object-storage-identities.json"))
            .Replace("${OBJECT_STORAGE_CATALOG_SECRET}", StorageSecret, StringComparison.Ordinal)
            .Replace("${OBJECT_STORAGE_IDENTITY_SECRET}", StorageSecret + "i", StringComparison.Ordinal);
        storage = new ContainerBuilder("chrislusf/seaweedfs:4.48")
            .WithCommand("server", "-dir=/data", "-ip=127.0.0.1", "-ip.bind=0.0.0.0", "-s3", "-s3.port=8333", "-s3.config=/etc/inklink/object-storage.json")
            .WithResourceMapping(System.Text.Encoding.UTF8.GetBytes(identities), "/etc/inklink/object-storage.json")
            .WithResourceMapping(System.Text.Encoding.UTF8.GetBytes(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "configure-object-storage.sh")).Replace("\r\n", "\n", StringComparison.Ordinal)), "/opt/inklink/configure-object-storage.sh")
            .WithPortBinding(8333, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(8333).ForPath("/healthz")))
            .Build();
        await storage.StartAsync();
        var buckets = await storage.ExecAsync(["sh", "/opt/inklink/configure-object-storage.sh"]);
        Assert.Equal(0, buckets.ExitCode);
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
        if (storage is not null) await storage.DisposeAsync();
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

    /// <summary>Envia uma obra por multipart (v2); com id, atualiza a obra existente.</summary>
    public Task<HttpResponseMessage> UploadArtworkAsync(string segment, string title, byte[]? image, Guid? id = null, string? price = null, (string Name, string Value)? extra = null)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(title), "title" },
            { new StringContent("Descrição de " + title), "alternativeText" },
            { new StringContent("Blackwork"), "style" },
        };
        if (price is not null) form.Add(new StringContent(price), "referencePrice");
        if (extra is { } field) form.Add(new StringContent(field.Value), field.Name);
        if (image is not null)
        {
            var file = new ByteArrayContent(image);
            file.Headers.ContentType = new("image/png");
            form.Add(file, "image", "obra.png");
        }
        var route = $"/api/v2/artists/me/{segment}" + (id is null ? string.Empty : "/" + id);
        return id is null ? Http.PostAsync(route, form) : Http.PutAsync(route, form);
    }

    /// <summary>Lê o objeto diretamente no SeaweedFS, como o proxy /media faria.</summary>
    public async Task<HttpResponseMessage> StorageGetAsync(string publicUrl)
    {
        var path = new Uri(publicUrl).AbsolutePath["/media/".Length..];
        using var http = new HttpClient();
        return await http.GetAsync(new Uri(Fixture.StorageEndpoint, path));
    }

    public async Task<int> CleanupCountAsync()
    {
        await using var connection = new NpgsqlConnection(OwnerConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM catalog.object_cleanups", connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    public Task<int> RunCleanupAsync() => Host.Services.GetRequiredService<InkLink.Catalog.Infrastructure.Media.ObjectCleanupWorker>().RunOnceAsync(default);

    public void Dispose() { Proxied.Dispose(); Gateway.Dispose(); Http.Dispose(); Host.Dispose(); }
}

internal static class TestImages
{
    public static byte[] Png(int width, int height)
    {
        using var bitmap = new SkiaSharp.SKBitmap(width, height);
        using (var canvas = new SkiaSharp.SKCanvas(bitmap)) canvas.Clear(new SkiaSharp.SKColor(200, 40, 50));
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
