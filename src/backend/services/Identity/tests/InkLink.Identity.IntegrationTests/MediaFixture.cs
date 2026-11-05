using System.Security.Cryptography;
using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace InkLink.Identity.IntegrationTests;

/// <summary>
/// SeaweedFS e RabbitMQ reais com os arquivos versionados de identidades, buckets, definições e usuários
/// do broker (decisão 0018). Usado pelos testes de foto de perfil.
/// </summary>
public sealed class MediaFixture : IAsyncLifetime
{
    private static string Secret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    public string IdentityStorageSecret { get; } = Secret();
    public string IdentityPublisherPassword { get; } = Secret();
    public string CatalogConsumerPassword { get; } = Secret();
    private readonly IContainer storage;
    private readonly IContainer broker;

    public MediaFixture()
    {
        static byte[] Asset(string name) => Encoding.UTF8.GetBytes(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Media", name)).Replace("\r\n", "\n", StringComparison.Ordinal));
        var identities = Encoding.UTF8.GetString(Asset("object-storage-identities.json"))
            .Replace("${OBJECT_STORAGE_CATALOG_SECRET}", Secret(), StringComparison.Ordinal)
            .Replace("${OBJECT_STORAGE_IDENTITY_SECRET}", IdentityStorageSecret, StringComparison.Ordinal);
        storage = new ContainerBuilder("chrislusf/seaweedfs:4.48")
            .WithCommand("server", "-dir=/data", "-ip=127.0.0.1", "-ip.bind=0.0.0.0", "-s3", "-s3.port=8333", "-s3.config=/etc/inklink/object-storage.json")
            .WithResourceMapping(Encoding.UTF8.GetBytes(identities), "/etc/inklink/object-storage.json")
            .WithResourceMapping(Asset("configure-object-storage.sh"), "/opt/inklink/configure-object-storage.sh")
            .WithPortBinding(8333, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(8333).ForPath("/healthz")))
            .Build();
        broker = new ContainerBuilder("rabbitmq:3.13.7-alpine")
            .WithEnvironment("RABBITMQ_DEFAULT_USER", "inklink")
            .WithEnvironment("RABBITMQ_DEFAULT_PASS", Secret())
            .WithEnvironment("RABBITMQ_PUBLISHER_PASS", Secret())
            .WithEnvironment("RABBITMQ_CONSUMER_PASS", Secret())
            .WithEnvironment("RABBITMQ_IDENTITY_PUBLISHER_PASS", IdentityPublisherPassword)
            .WithEnvironment("RABBITMQ_CATALOG_CONSUMER_PASS", CatalogConsumerPassword)
            .WithEnvironment("RABBITMQ_SERVER_ADDITIONAL_ERL_ARGS", "+S 2:2")
            .WithEnvironment("RABBITMQ_CTL_ERL_ARGS", "+S 1:1")
            .WithPortBinding(5672, true)
            .WithResourceMapping(Asset("configure-broker.sh"), "/opt/inklink/configure-broker.sh")
            .WithResourceMapping(Asset("broker-definitions.json"), "/opt/inklink/broker-definitions.json")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Server startup complete"))
            .Build();
    }

    public Uri StorageEndpoint => new($"http://127.0.0.1:{storage.GetMappedPublicPort(8333)}/");
    public string PublisherUri => $"amqp://inklink_identity_publisher:{IdentityPublisherPassword}@{broker.Hostname}:{broker.GetMappedPublicPort(5672)}/";
    public string CatalogConsumerUri => $"amqp://inklink_catalog_consumer:{CatalogConsumerPassword}@{broker.Hostname}:{broker.GetMappedPublicPort(5672)}/";

    public async Task InitializeAsync()
    {
        // A limpeza é acionada explicitamente pelos testes.
        Infrastructure.Media.ObjectCleanupWorker.Interval = TimeSpan.FromHours(1);
        await Task.WhenAll(storage.StartAsync(), broker.StartAsync());
        var buckets = await storage.ExecAsync(["sh", "/opt/inklink/configure-object-storage.sh"]);
        Assert.True(buckets.ExitCode == 0, buckets.Stderr);
        var users = await broker.ExecAsync(["sh", "/opt/inklink/configure-broker.sh"]);
        Assert.True(users.ExitCode == 0, users.Stderr);
    }

    /// <summary>Lê o objeto como o proxy /media local: remove o prefixo e encaminha ao endpoint.</summary>
    public async Task<HttpResponseMessage> StorageGetAsync(string browserUrl)
    {
        var uri = new Uri(browserUrl);
        using var http = new HttpClient();
        return await http.GetAsync(new Uri(StorageEndpoint, uri.PathAndQuery["/media/".Length..]));
    }

    public async Task DisposeAsync()
    {
        await storage.DisposeAsync();
        await broker.DisposeAsync();
    }
}

internal sealed class PhotoIdentityHost(SecurityFixture fixture, MediaFixture media) : WebApplicationFactory<IdentityProgram>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Testing")
        .UseSecuritySettings(SecurityConfiguration.Identity(fixture).Concat(new Dictionary<string, string?>
        {
            ["ObjectStorage:ServiceUrl"] = media.StorageEndpoint.ToString(),
            ["ObjectStorage:AccessKeyId"] = "inklink-identity",
            ["ObjectStorage:SecretAccessKey"] = media.IdentityStorageSecret,
            ["ObjectStorage:PublicBucket"] = "inklink-identity-media",
            ["ObjectStorage:PublicBaseUrl"] = "https://localhost:5173/media/inklink-identity-media",
            ["ObjectStorage:PrivateBucket"] = "inklink-identity-private",
            ["ObjectStorage:SignedUrlBaseUrl"] = "https://localhost:5173/media",
            ["Broker:Uri"] = media.PublisherUri,
        }))
        .ConfigureTestServices(services => services.Configure<JwtBearerOptions>("Bearer", options => options.BackchannelHttpHandler = fixture.CreateProviderHandler()));
}
