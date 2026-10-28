using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace InkLink.Identity.IntegrationTests;

[Collection("security")]
public sealed class ObservabilityTests(SecurityFixture fixture)
{
    [Theory]
    [InlineData("Gateway", false)]
    [InlineData("Bff", false)]
    [InlineData("Gateway", true)]
    [InlineData("Bff", true)]
    public async Task Errors_are_correlated_in_structured_logs_and_responses(string component, bool failRoute)
    {
        using var identity = new SecurityIdentityHost(fixture);
        using var logs = new RecordedLogs();
        using var gateway = new SecurityGatewayHost(fixture, identity.Server.CreateHandler(), component == "Gateway" ? logs : null, failRoute);
        using var bff = new SecurityBffHost(fixture, gateway.Server.CreateHandler(), component == "Bff" ? logs : null, failRoute);
        using var client = component == "Gateway" ? gateway.CreateClient() : bff.CreateClient();
        var path = failRoute ? "/__tests/failure" : component == "Gateway" ? "/api/v1/profiles/me" : "/bff/session";
        var response = await client.GetAsync(path + "?private=private-query-marker");
        Assert.Equal(failRoute ? HttpStatusCode.InternalServerError : HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
        var traceId = response.Headers.GetValues("X-Trace-Id").Single();
        Assert.Equal(traceId, problem["traceId"]!.GetValue<string>());
        var entry = Assert.Single(logs.Entries, entry => entry.Category.EndsWith("RequestLoggingMiddleware", StringComparison.Ordinal));
        Assert.Equal("GET", entry.State["Method"]);
        Assert.Equal(path, entry.State["Path"]);
        Assert.Equal((int)response.StatusCode, entry.State["StatusCode"]);
        Assert.IsType<double>(entry.State["ElapsedMs"]);
        Assert.Equal(traceId, entry.Scope["traceId"]);
        Assert.Equal("InkLink." + component, entry.Scope["service"]);
        Assert.All(logs.Entries, entry =>
        {
            Assert.DoesNotContain("private-query-marker", entry.Message);
            Assert.DoesNotContain("private-failure-marker", entry.Message);
        });
        var services = component == "Gateway" ? gateway.Services : bff.Services;
        Assert.True(services.GetRequiredService<IOptionsMonitor<JsonConsoleFormatterOptions>>().CurrentValue.IncludeScopes);
    }

    [Fact]
    public async Task Successful_proxy_requests_are_logged_without_query_values_or_access_tokens()
    {
        using var identity = new SecurityIdentityHost(fixture);
        using var logs = new RecordedLogs();
        using var gateway = new SecurityGatewayHost(fixture, identity.Server.CreateHandler(), logs);
        using var client = gateway.CreateClient();
        var token = (await fixture.TokenAsync())["access_token"]!.GetValue<string>();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var response = await client.GetAsync("/api/v1/profiles/me?private=private-query-marker");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await response.Content.ReadAsStringAsync();
        var entry = Assert.Single(logs.Entries, entry => entry.Category.EndsWith("RequestLoggingMiddleware", StringComparison.Ordinal));
        Assert.Equal(200, entry.State["StatusCode"]);
        Assert.Equal(response.Headers.GetValues("X-Trace-Id").Single(), entry.Scope["traceId"]);
        Assert.All(logs.Entries, entry =>
        {
            Assert.DoesNotContain("private-query-marker", entry.Message);
            Assert.DoesNotContain(token, entry.Message);
        });
    }
}
