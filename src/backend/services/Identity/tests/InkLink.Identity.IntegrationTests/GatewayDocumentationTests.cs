using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using InkLink.Gateway.Documentation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace InkLink.Identity.IntegrationTests;

public sealed class GatewayDocumentationTests
{
    [Theory]
    [InlineData("identity", "/api/v1/profiles/me")]
    [InlineData("catalog", "/api/v1/artists")]
    [InlineData("engagements", "/api/v2/engagements")]
    [InlineData("messaging", "/api/v1/conversations")]
    [InlineData("scheduling", "/api/v1/scheduling/reservations")]
    public async Task Public_documents_are_selectable_filtered_and_use_gateway_origin(string name, string path)
    {
        var handler = new DocumentsHandler(path);
        using var host = new DocumentationHost(handler); using var http = host.CreateClient();
        Assert.Contains("InkLink", await http.GetStringAsync("/swagger/index.html"));
        var script = await http.GetStringAsync("/swagger/index.js");
        Assert.Contains("inklink-swagger", script); Assert.Contains("usePkceWithAuthorizationCodeGrant", script);
        Assert.DoesNotContain("clientSecret", script); Assert.DoesNotContain("persistAuthorization\":true", script);
        foreach (var service in new[] { "identity", "catalog", "engagements", "messaging", "scheduling" }) Assert.Contains(service + ".json", script);
        var document = (await http.GetFromJsonAsync<JsonObject>($"/swagger/documents/{name}.json"))!;
        Assert.Equal("/", document["servers"]![0]!["url"]!.GetValue<string>());
        Assert.NotNull(document["paths"]![path]);
        Assert.Null(document["paths"]!["/api/v1/internal/profiles/{id}"]);
        Assert.Null(document["paths"]![path]!["servers"]);
        Assert.Null(document["paths"]![path]!["get"]!["servers"]);
        Assert.Null(document["components"]!["schemas"]!["PrivateContract"]);
        Assert.NotNull(document["components"]!["schemas"]!["PublicContract"]);
        Assert.Equal("oauth2", document["components"]!["securitySchemes"]!["Bearer"]!["type"]!.GetValue<string>());
        Assert.False(handler.ReceivedCredentials);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/swagger/documents/arbitrary.json")).StatusCode);
        // Swagger is a client of the existing protected API, never an authorization bypass.
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/v1/profiles/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/api/v1/internal/engagements/00000000-0000-0000-0000-000000000001/scheduling-context")).StatusCode);
    }
    [Fact]
    public async Task Unavailable_document_is_explicit_and_other_documents_still_work()
    {
        var handler = new DocumentsHandler("/api/v1/profiles/me") { FailHost = "catalog.test" };
        using var host = new DocumentationHost(handler); using var http = host.CreateClient();
        var failed = await http.GetAsync("/swagger/documents/catalog.json");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.Contains("documentation.service_unavailable", await failed.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/swagger/documents/identity.json")).StatusCode);
    }
    [Fact]
    public async Task Documentation_and_assets_are_absent_in_production()
    {
        using var host = new DocumentationHost(new DocumentsHandler("/api/v1/profiles/me"), "Production"); using var http = host.CreateClient();
        foreach (var path in new[] { "/swagger/index.html", "/swagger/swagger-ui-bundle.js", "/swagger/documents/identity.json" })
            Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync(path)).StatusCode);
    }
    private sealed class DocumentationHost(DocumentsHandler handler, string environment = "Testing") : WebApplicationFactory<GatewayProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("Authentication:Authority", "https://issuer.test/realms/inklink");
            builder.UseSetting("Authentication:Audience", "inklink-api");
            builder.UseSetting("Authentication:UserClients:0", "inklink-swagger");
            foreach (var service in new[] { "Identity", "Catalog", "Engagements", "Messaging", "Scheduling" })
                builder.UseSetting("Gateway:" + service + "Address", "https://" + service.ToLowerInvariant() + ".test/");
            builder.ConfigureTestServices(s => s.AddHttpClient<GatewayDocuments>().ConfigurePrimaryHttpMessageHandler(() => handler));
        }
    }
    private sealed class DocumentsHandler(string publicPath) : HttpMessageHandler
    {
        public string? FailHost { get; init; }
        public bool ReceivedCredentials { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ReceivedCredentials |= request.Headers.Authorization is not null || request.Headers.Contains("Cookie");
            if (request.RequestUri!.Host == FailHost) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new JsonObject
                {
                    ["openapi"] = "3.1.0",
                    ["info"] = new JsonObject { ["title"] = "Service", ["version"] = "v1" },
                    ["servers"] = new JsonArray(new JsonObject { ["url"] = "https://private.test/" }),
                    ["paths"] = new JsonObject
                    {
                        [publicPath] = new JsonObject
                        {
                            ["servers"] = new JsonArray(new JsonObject { ["url"] = "https://private.test/" }),
                            ["get"] = new JsonObject
                            {
                                ["servers"] = new JsonArray(new JsonObject { ["url"] = "https://private.test/" }),
                                ["responses"] = new JsonObject { ["200"] = new JsonObject { ["content"] = new JsonObject { ["application/json"] = new JsonObject { ["schema"] = new JsonObject { ["$ref"] = "#/components/schemas/PublicContract" } } } } }
                            }
                        },
                        ["/api/v1/internal/profiles/{id}"] = new JsonObject { ["get"] = new JsonObject { ["responses"] = new JsonObject { ["200"] = new JsonObject { ["$ref"] = "#/components/schemas/PrivateContract" } } } }
                    },
                    ["components"] = new JsonObject { ["schemas"] = new JsonObject { ["PublicContract"] = new JsonObject { ["type"] = "object" }, ["PrivateContract"] = new JsonObject { ["type"] = "object" } } }
                })
            });
        }
    }
}
