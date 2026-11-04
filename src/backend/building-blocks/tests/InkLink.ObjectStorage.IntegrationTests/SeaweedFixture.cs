using System.Security.Cryptography;
using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Options;

namespace InkLink.ObjectStorage.IntegrationTests;

/// <summary>
/// SeaweedFS real com as mesmas identidades e o mesmo script de buckets dos ambientes local e P09.
/// </summary>
public sealed class SeaweedFixture : IAsyncLifetime
{
    public const string CatalogBucket = "inklink-catalog-media";
    public const string IdentityPublicBucket = "inklink-identity-media";
    public const string IdentityPrivateBucket = "inklink-identity-private";

    public string CatalogSecret { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    public string IdentitySecret { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    public IContainer Container { get; }
    public Uri Endpoint => new($"http://127.0.0.1:{Container.GetMappedPublicPort(8333)}/");

    public SeaweedFixture()
    {
        var identities = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "object-storage-identities.json"))
            .Replace("${OBJECT_STORAGE_CATALOG_SECRET}", CatalogSecret, StringComparison.Ordinal)
            .Replace("${OBJECT_STORAGE_IDENTITY_SECRET}", IdentitySecret, StringComparison.Ordinal);
        var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "configure-object-storage.sh")).Replace("\r\n", "\n", StringComparison.Ordinal);
        Container = new ContainerBuilder("chrislusf/seaweedfs:4.48")
            .WithCommand("server", "-dir=/data", "-ip=127.0.0.1", "-ip.bind=0.0.0.0", "-s3", "-s3.port=8333", "-s3.config=/etc/inklink/object-storage.json")
            .WithResourceMapping(Encoding.UTF8.GetBytes(identities), "/etc/inklink/object-storage.json")
            .WithResourceMapping(Encoding.UTF8.GetBytes(script), "/opt/inklink/configure-object-storage.sh")
            .WithPortBinding(8333, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(8333).ForPath("/healthz")))
            .Build();
    }

    public async Task InitializeAsync()
    {
        await Container.StartAsync();
        var result = await ConfigureBucketsAsync();
        if (result.ExitCode != 0) throw new InvalidOperationException("Bucket configuration failed: " + result.Stdout + result.Stderr);
    }

    public Task<ExecResult> ConfigureBucketsAsync() => Container.ExecAsync(["sh", "/opt/inklink/configure-object-storage.sh"]);

    public ObjectStorageOptions CatalogOptions(Action<ObjectStorageOptions>? configure = null) => Options("inklink-catalog", CatalogSecret, CatalogBucket, null, configure);

    public ObjectStorageOptions IdentityOptions(Action<ObjectStorageOptions>? configure = null) =>
        Options("inklink-identity", IdentitySecret, IdentityPublicBucket, IdentityPrivateBucket, configure);

    private ObjectStorageOptions Options(string accessKey, string secret, string publicBucket, string? privateBucket, Action<ObjectStorageOptions>? configure)
    {
        var options = new ObjectStorageOptions
        {
            ServiceUrl = Endpoint.ToString(),
            AccessKeyId = accessKey,
            SecretAccessKey = secret,
            PublicBucket = publicBucket,
            PublicBaseUrl = "https://localhost:5173/media/" + publicBucket,
            PrivateBucket = privateBucket ?? string.Empty,
        };
        configure?.Invoke(options);
        options.Validate(localEnvironment: true);
        return options;
    }

    public static S3ObjectStorage Storage(ObjectStorageOptions options) => new(Microsoft.Extensions.Options.Options.Create(options));

    public Task DisposeAsync() => Container.DisposeAsync().AsTask();
}

[CollectionDefinition("seaweed")]
public sealed class SeaweedCollection : ICollectionFixture<SeaweedFixture>;
