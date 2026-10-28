using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Nodes;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using InkLink.Bff;
using Microsoft.EntityFrameworkCore;

namespace InkLink.Identity.IntegrationTests;

public sealed class SecurityFixture : IAsyncLifetime
{
    public PostgresFixture Database { get; } = new();
    private IContainer keycloak = null!;
    public string Password { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    public string BffSecret { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    public string ServiceSecret { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    public string CertificateThumbprint { get; private set; } = string.Empty;
    public string Authority { get; private set; } = string.Empty;
    public string BffConnection { get; private set; } = string.Empty;
    public string CertificatePath { get; } = Path.Combine(Path.GetTempPath(), "inklink-" + Guid.NewGuid() + ".pfx");
    public string CertificatePassword { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

    public async Task InitializeAsync()
    {
        await Database.InitializeAsync();
        var connections = await Database.CreateBffDatabaseAsync();
        BffConnection = connections.App;
        var options = new DbContextOptionsBuilder<BffDbContext>();
        BffDbContext.Configure(options, connections.Owner);
        await using (var database = new BffDbContext(options.Options)) await database.Database.MigrateAsync();
        byte[] certificatePem;
        byte[] privateKeyPem;
        using (var rsa = RSA.Create(2048))
        {
            var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var names = new SubjectAlternativeNameBuilder();
            names.AddDnsName("localhost");
            names.AddIpAddress(IPAddress.Loopback);
            request.CertificateExtensions.Add(names.Build());
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
            await File.WriteAllBytesAsync(CertificatePath, certificate.Export(X509ContentType.Pfx, CertificatePassword));
            CertificateThumbprint = certificate.Thumbprint;
            certificatePem = Encoding.UTF8.GetBytes(certificate.ExportCertificatePem());
            privateKeyPem = Encoding.UTF8.GetBytes(rsa.ExportPkcs8PrivateKeyPem());
        }
        var realm = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "inklink-realm.json")))!;
        var clients = realm["clients"]!.AsArray();
        clients[0]!["secret"] = BffSecret;
        clients[3]!["secret"] = ServiceSecret;
        var testClient = clients[1]!.DeepClone();
        testClient["clientId"] = "inklink-test-users";
        testClient["directAccessGrantsEnabled"] = true;
        testClient["standardFlowEnabled"] = false;
        clients.Add(testClient);
        var shortClient = testClient.DeepClone();
        shortClient["clientId"] = "inklink-test-short";
        shortClient["attributes"]!["access.token.lifespan"] = "1";
        clients.Add(shortClient);
        var wrongAudience = testClient.DeepClone();
        wrongAudience["clientId"] = "inklink-test-other-audience";
        wrongAudience["protocolMappers"]![0]!["config"]!["included.custom.audience"] = "other-api";
        clients.Add(wrongAudience);
        realm["users"] = new JsonArray("alice", "bob", "unprovisioned").Select(name => (JsonNode?)new JsonObject
        {
            ["username"] = name!.GetValue<string>(),
            ["enabled"] = true,
            ["emailVerified"] = true,
            ["email"] = name.GetValue<string>() + "@example.test",
            ["firstName"] = name.GetValue<string>(),
            ["lastName"] = "Test",
            ["credentials"] = new JsonArray(new JsonObject { ["type"] = "password", ["value"] = Password, ["temporary"] = false })
        }).ToJsonArray();
        keycloak = new ContainerBuilder("quay.io/keycloak/keycloak:26.8.1")
            .WithPortBinding(8443, true)
            .WithEnvironment("KC_HTTP_ENABLED", "false")
            .WithEnvironment("KC_HTTPS_CERTIFICATE_FILE", "/opt/keycloak/conf/test.pem")
            .WithEnvironment("KC_HTTPS_CERTIFICATE_KEY_FILE", "/opt/keycloak/conf/test.key")
            .WithResourceMapping(certificatePem, "/opt/keycloak/conf/test.pem")
            .WithResourceMapping(privateKeyPem, "/opt/keycloak/conf/test.key")
            .WithEnvironment("KC_DB", "dev-mem")
            .WithEnvironment("JAVA_OPTS_KC_HEAP", "-Xms128m -Xmx512m")
            .WithCommand("start-dev", "--import-realm")
            .WithResourceMapping(Encoding.UTF8.GetBytes(realm.ToJsonString()), "/opt/keycloak/data/import/inklink-realm.json")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Listening on:"))
            .Build();
        await keycloak.StartAsync();
        Authority = $"https://{keycloak.Hostname}:{keycloak.GetMappedPublicPort(8443)}/realms/inklink";
    }

    public async Task<JsonObject> TokenAsync(string user = "alice", string client = "inklink-test-users", bool service = false, bool scoped = true)
    {
        using var http = new HttpClient(CreateProviderHandler());
        var values = service
            ? new Dictionary<string, string> { ["grant_type"] = "client_credentials", ["client_id"] = "inklink-catalog", ["client_secret"] = ServiceSecret }
            : new Dictionary<string, string> { ["grant_type"] = "password", ["client_id"] = client, ["username"] = user, ["password"] = Password };
        if (!service) values["scope"] = "openid profile";
        else if (scoped) values["scope"] = "identity.profile.read";
        var response = await http.PostAsync(Authority + "/protocol/openid-connect/token", new FormUrlEncodedContent(values));
        if (!response.IsSuccessStatusCode)
        {
            var error = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
            Assert.Fail("Token endpoint error: " + error["error"] + ": " + error["error_description"]);
        }
        var result = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        return result;
    }

    public HttpClientHandler CreateProviderHandler(CookieContainer? cookies = null) => new()
    {
        CookieContainer = cookies ?? new(),
        AllowAutoRedirect = false,
        ServerCertificateCustomValidationCallback = (request, certificate, _, _) =>
            request.RequestUri?.IsLoopback == true && certificate?.GetCertHashString() == CertificateThumbprint
    };

    public async Task DisposeAsync()
    {
        if (keycloak is not null) await keycloak.DisposeAsync();
        await Database.DisposeAsync();
        File.Delete(CertificatePath);
    }
}

internal static class JsonNodeExtensions
{
    public static JsonArray ToJsonArray(this IEnumerable<JsonNode?> nodes) => new(nodes.ToArray());
}

[CollectionDefinition("security")]
public sealed class SecurityCollection : ICollectionFixture<SecurityFixture>;
