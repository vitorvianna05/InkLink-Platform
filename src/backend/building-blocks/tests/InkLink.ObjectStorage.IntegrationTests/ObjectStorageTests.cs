using System.Net;
using Amazon.S3;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace InkLink.ObjectStorage.IntegrationTests;

[Collection("seaweed")]
public sealed class ObjectStorageTests(SeaweedFixture fixture)
{
    private static readonly byte[] Image = [0x52, 0x49, 0x46, 0x46, 0x10, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50];
    private readonly HttpClient anonymous = new();

    private Uri Direct(string bucket, string key) => new(fixture.Endpoint, bucket + "/" + key);
    private static string NewKey(string prefix) => $"{prefix}/{Guid.CreateVersion7():N}.webp";

    [Fact]
    public async Task Public_object_is_readable_anonymously_with_immutable_cache_and_public_url()
    {
        using var storage = SeaweedFixture.Storage(fixture.CatalogOptions());
        var key = NewKey("artworks");
        await storage.PutAsync(ObjectVisibility.Public, key, new MemoryStream(Image), "image/webp", CancellationToken.None);

        using var response = await anonymous.GetAsync(Direct(SeaweedFixture.CatalogBucket, key));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Image, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal("image/webp", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("public, max-age=31536000, immutable", response.Headers.CacheControl?.ToString());
        Assert.Equal($"https://localhost:5173/media/{SeaweedFixture.CatalogBucket}/{key}", storage.GetPublicUrl(key).AbsoluteUri);
    }

    [Fact]
    public async Task Private_object_is_denied_anonymously_and_readable_only_by_signed_url()
    {
        using var storage = SeaweedFixture.Storage(fixture.IdentityOptions());
        var key = NewKey("profiles");
        await storage.PutAsync(ObjectVisibility.Private, key, new MemoryStream(Image), "image/webp", CancellationToken.None);

        using var denied = await anonymous.GetAsync(Direct(SeaweedFixture.IdentityPrivateBucket, key));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var signed = await storage.CreateSignedReadUrlAsync(key, CancellationToken.None);
        Assert.Contains("X-Amz-Signature=", signed.Query, StringComparison.Ordinal);
        using var allowed = await anonymous.GetAsync(signed);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Equal(Image, await allowed.Content.ReadAsByteArrayAsync());

        var tampered = new Uri(signed.AbsoluteUri.Replace(key, NewKey("profiles"), StringComparison.Ordinal));
        using var other = await anonymous.GetAsync(tampered);
        Assert.Equal(HttpStatusCode.Forbidden, other.StatusCode);
    }

    [Fact]
    public async Task Signed_url_expires_after_its_lifetime()
    {
        using var storage = SeaweedFixture.Storage(fixture.IdentityOptions(options => options.SignedUrlLifetime = TimeSpan.FromSeconds(1)));
        var key = NewKey("profiles");
        await storage.PutAsync(ObjectVisibility.Private, key, new MemoryStream(Image), "image/webp", CancellationToken.None);
        var signed = await storage.CreateSignedReadUrlAsync(key, CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(3));
        using var expired = await anonymous.GetAsync(signed);
        Assert.Equal(HttpStatusCode.Forbidden, expired.StatusCode);
    }

    [Fact]
    public async Task Signed_url_on_the_browser_base_stays_valid_when_the_local_proxy_forwards_it()
    {
        using var storage = SeaweedFixture.Storage(fixture.IdentityOptions(options => options.SignedUrlBaseUrl = "https://localhost:5173/media"));
        var key = NewKey("profiles");
        await storage.PutAsync(ObjectVisibility.Private, key, new MemoryStream(Image), "image/webp", CancellationToken.None);
        var signed = await storage.CreateSignedReadUrlAsync(key, CancellationToken.None);
        Assert.StartsWith($"https://localhost:5173/media/{SeaweedFixture.IdentityPrivateBucket}/{key}?", signed.AbsoluteUri, StringComparison.Ordinal);

        // O proxy /media remove o prefixo e encaminha ao endpoint com o host do endpoint (changeOrigin).
        var forwarded = new Uri(fixture.Endpoint, signed.PathAndQuery["/media".Length..]);
        using var response = await anonymous.GetAsync(forwarded);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Service_credentials_are_limited_to_their_own_buckets()
    {
        using var catalog = SeaweedFixture.Storage(fixture.CatalogOptions(options => options.PublicBucket = SeaweedFixture.IdentityPublicBucket));
        var denied = await Assert.ThrowsAsync<AmazonS3Exception>(() =>
            catalog.PutAsync(ObjectVisibility.Public, NewKey("intrusion"), new MemoryStream(Image), "image/webp", CancellationToken.None));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        using var identity = SeaweedFixture.Storage(fixture.IdentityOptions(options => options.PublicBucket = SeaweedFixture.CatalogBucket));
        var alsoDenied = await Assert.ThrowsAsync<AmazonS3Exception>(() =>
            identity.PutAsync(ObjectVisibility.Public, NewKey("intrusion"), new MemoryStream(Image), "image/webp", CancellationToken.None));
        Assert.Equal(HttpStatusCode.Forbidden, alsoDenied.StatusCode);

        using var catalogWithoutPrivate = SeaweedFixture.Storage(fixture.CatalogOptions());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            catalogWithoutPrivate.PutAsync(ObjectVisibility.Private, NewKey("x"), new MemoryStream(Image), "image/webp", CancellationToken.None));
    }

    [Fact]
    public async Task Delete_removes_the_object_and_repeating_it_is_not_an_error()
    {
        using var storage = SeaweedFixture.Storage(fixture.CatalogOptions());
        var key = NewKey("artworks");
        await storage.PutAsync(ObjectVisibility.Public, key, new MemoryStream(Image), "image/webp", CancellationToken.None);
        await storage.DeleteAsync(ObjectVisibility.Public, key, CancellationToken.None);
        await storage.DeleteAsync(ObjectVisibility.Public, key, CancellationToken.None);
        using var response = await anonymous.GetAsync(Direct(SeaweedFixture.CatalogBucket, key));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Health_check_distinguishes_valid_and_rejected_credentials()
    {
        using var healthy = new ObjectStorageHealthCheck(Options.Create(fixture.CatalogOptions()));
        Assert.Equal(HealthStatus.Healthy, (await healthy.CheckHealthAsync(new HealthCheckContext())).Status);
        using var rejected = new ObjectStorageHealthCheck(Options.Create(fixture.CatalogOptions(options => options.SecretAccessKey = "wrong-secret")));
        Assert.Equal(HealthStatus.Unhealthy, (await rejected.CheckHealthAsync(new HealthCheckContext())).Status);
    }

    [Fact]
    public async Task Bucket_configuration_script_is_idempotent()
    {
        var result = await fixture.ConfigureBucketsAsync();
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("bucket inklink-identity-private already exists", result.Stdout, StringComparison.Ordinal);
    }
}

public sealed class ObjectStorageOptionsTests
{
    private static ObjectStorageOptions Valid() => new()
    {
        ServiceUrl = "https://account.r2.cloudflarestorage.com",
        Region = "auto",
        AccessKeyId = "key",
        SecretAccessKey = "secret",
        PublicBucket = "inklink-catalog-media",
        PublicBaseUrl = "https://media.inklink.example",
    };

    [Fact]
    public void Production_configuration_for_r2_is_accepted() => Valid().Validate(localEnvironment: false);

    [Theory]
    [InlineData("http://127.0.0.1:5188", false)]
    [InlineData("http://storage.internal:8333", true)]
    [InlineData("https://user:pass@account.r2.cloudflarestorage.com", false)]
    [InlineData("not-a-url", true)]
    public void Endpoint_must_be_https_except_loopback_in_local_environments(string serviceUrl, bool local)
    {
        var options = Valid();
        options.ServiceUrl = serviceUrl;
        Assert.Throws<InvalidOperationException>(() => options.Validate(local));
    }

    [Fact]
    public void Loopback_http_endpoint_is_accepted_locally()
    {
        var options = Valid();
        options.ServiceUrl = "http://127.0.0.1:5188";
        options.Validate(localEnvironment: true);
    }

    [Theory]
    [InlineData("http://localhost:5173/media")]
    [InlineData("https://localhost:5173/media?x=1")]
    [InlineData("")]
    public void Browser_bases_must_be_https(string value)
    {
        var options = Valid();
        options.PublicBaseUrl = value;
        Assert.Throws<InvalidOperationException>(() => options.Validate(localEnvironment: true));
    }

    [Fact]
    public void Private_bucket_must_differ_and_lifetime_is_bounded()
    {
        var same = Valid();
        same.PrivateBucket = same.PublicBucket;
        Assert.Throws<InvalidOperationException>(() => same.Validate(false));
        var tooLong = Valid();
        tooLong.SignedUrlLifetime = TimeSpan.FromHours(2);
        Assert.Throws<InvalidOperationException>(() => tooLong.Validate(false));
    }

    [Theory]
    [InlineData("../secret")]
    [InlineData("/artworks/a.webp")]
    [InlineData("artworks/../a.webp")]
    [InlineData("Artworks/A.webp")]
    [InlineData("artworks/a b.webp")]
    [InlineData("artworks//a.webp")]
    [InlineData("")]
    public void Keys_outside_the_generated_format_are_rejected(string key) =>
        Assert.Throws<ArgumentException>(() => S3ObjectStorage.ValidateKey(key));

    [Fact]
    public void Generated_keys_are_accepted() => S3ObjectStorage.ValidateKey("artworks/0196f0aa-1b2c-7d3e-8f40-123456789abc/1600.webp");
}
