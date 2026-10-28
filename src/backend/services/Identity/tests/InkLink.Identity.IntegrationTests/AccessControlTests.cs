using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using InkLink.Identity.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InkLink.Identity.IntegrationTests;

[Collection("security")]
public sealed class AccessControlTests(SecurityFixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_invalid_expired_and_id_tokens_are_rejected(bool throughGateway)
    {
        using var identity = new SecurityIdentityHost(fixture);
        using var gateway = new SecurityGatewayHost(fixture, identity.Server.CreateHandler());
        using var client = throughGateway ? gateway.CreateClient() : identity.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/profiles/me")).StatusCode);
        var tokens = await fixture.TokenAsync();
        var parts = tokens["access_token"]!.GetValue<string>().Split('.');
        parts[2] = (parts[2][0] == 'A' ? "B" : "A") + parts[2][1..];
        var badSignature = string.Join('.', parts);
        foreach (var invalid in new[] { "invalid", badSignature, tokens["id_token"]!.GetValue<string>(), (await fixture.TokenAsync(client: "inklink-test-other-audience"))["access_token"]!.GetValue<string>() })
        {
            client.DefaultRequestHeaders.Authorization = new("Bearer", invalid);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/profiles/me")).StatusCode);
        }
        var shortLived = (await fixture.TokenAsync(client: "inklink-test-short"))["access_token"]!.GetValue<string>();
        await Task.Delay(TimeSpan.FromSeconds(7));
        client.DefaultRequestHeaders.Authorization = new("Bearer", shortLived);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/profiles/me")).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Accounts_are_stable_isolated_and_roles_cannot_be_changed_by_request(bool throughGateway)
    {
        using var identity = new SecurityIdentityHost(fixture);
        using var gateway = new SecurityGatewayHost(fixture, identity.Server.CreateHandler());
        using var client = throughGateway ? gateway.CreateClient() : identity.CreateClient();
        var aliceToken = (await fixture.TokenAsync())["access_token"]!.GetValue<string>();
        var bobToken = (await fixture.TokenAsync("bob"))["access_token"]!.GetValue<string>();
        client.DefaultRequestHeaders.Authorization = new("Bearer", aliceToken);
        var alice = (await client.GetFromJsonAsync<JsonObject>("/api/v1/profiles/me"))!;
        var id = alice["id"]!.GetValue<Guid>();
        Assert.Equal(id, (await client.GetFromJsonAsync<JsonObject>("/api/v1/profiles/me"))!["id"]!.GetValue<Guid>());
        client.DefaultRequestHeaders.Authorization = new("Bearer", bobToken);
        var bob = (await client.GetFromJsonAsync<JsonObject>("/api/v1/profiles/me"))!;
        Assert.NotEqual(id, bob["id"]!.GetValue<Guid>());
        client.DefaultRequestHeaders.Add("X-User-Id", id.ToString());
        client.DefaultRequestHeaders.Add("X-Roles", "Artist");
        Assert.Equal(bob["id"]!.GetValue<Guid>(), (await client.GetFromJsonAsync<JsonObject>("/api/v1/profiles/me"))!["id"]!.GetValue<Guid>());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/profiles/" + id)).StatusCode);
        var crafted = await client.PutAsJsonAsync("/api/v1/profiles/me", new { displayName = "Bob", id, isArtist = true, roles = new[] { "Artist" } });
        Assert.Equal(HttpStatusCode.BadRequest, crafted.StatusCode);
        var invalid = await client.PutAsJsonAsync("/api/v1/profiles/me", new { displayName = " " });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var invalidBody = JsonNode.Parse(await invalid.Content.ReadAsStringAsync())!;
        Assert.NotNull(invalidBody["errors"]?["displayName"]);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync("/api/v1/profiles/me", new { displayName = "Bob atualizado" })).StatusCode);
        var updated = (await client.GetFromJsonAsync<JsonObject>("/api/v1/profiles/me"))!;
        Assert.Equal("Bob atualizado", updated["displayName"]!.GetValue<string>());
        Assert.Equal(new[] { "Client" }, updated["roles"]!.AsArray().Select(role => role!.GetValue<string>()));
        client.DefaultRequestHeaders.Authorization = new("Bearer", aliceToken);
        Assert.NotEqual("Bob atualizado", (await client.GetFromJsonAsync<JsonObject>("/api/v1/profiles/me"))!["displayName"]!.GetValue<string>());
        await using var database = fixture.Database.CreateOwnerContext();
        Assert.Equal(1, await database.ExternalAccounts.CountAsync(account => account.ProfileId == id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Own_profile_with_trailing_slash_uses_the_same_identity_and_accepts_updates(bool throughGateway)
    {
        using var identity = new SecurityIdentityHost(fixture);
        using var gateway = new SecurityGatewayHost(fixture, identity.Server.CreateHandler());
        using var client = throughGateway ? gateway.CreateClient() : identity.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", (await fixture.TokenAsync())["access_token"]!.GetValue<string>());
        var canonical = (await client.GetFromJsonAsync<JsonObject>("/api/v1/profiles/me"))!;
        var alternate = (await client.GetFromJsonAsync<JsonObject>("/api/v1/profiles/me/"))!;
        Assert.Equal(canonical["id"]!.GetValue<Guid>(), alternate["id"]!.GetValue<Guid>());
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync("/api/v1/profiles/me/", new { displayName = "Updated via alternate route" })).StatusCode);
        Assert.Equal("Updated via alternate route", (await client.GetFromJsonAsync<JsonObject>("/api/v1/profiles/me"))!["displayName"]!.GetValue<string>());
    }

    [Fact]
    public async Task Unsupported_method_does_not_provision_an_account()
    {
        using var identity = new SecurityIdentityHost(fixture);
        using var client = identity.CreateClient();
        await using var database = fixture.Database.CreateOwnerContext();
        var before = await database.ExternalAccounts.CountAsync();
        client.DefaultRequestHeaders.Authorization = new("Bearer", (await fixture.TokenAsync("unprovisioned"))["access_token"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.DeleteAsync("/api/v1/profiles/me")).StatusCode);
        Assert.Equal(before, await database.ExternalAccounts.CountAsync());
    }

    [Fact]
    public async Task Only_administrative_activation_adds_artist_without_removing_client()
    {
        using var identity = new SecurityIdentityHost(fixture);
        using var client = identity.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", (await fixture.TokenAsync())["access_token"]!.GetValue<string>());
        var profileResponse = await client.GetAsync("/api/v1/profiles/me");
        Assert.True(profileResponse.IsSuccessStatusCode, "Profile response: " + await profileResponse.Content.ReadAsStringAsync());
        var profile = (await profileResponse.Content.ReadFromJsonAsync<JsonObject>())!;
        var id = profile["id"]!.GetValue<Guid>();
        await using var database = fixture.Database.CreateOwnerContext();
        var link = await database.ExternalAccounts.SingleAsync(account => account.ProfileId == id);
        using (var scope = identity.Services.CreateScope())
            Assert.True(await new ActivateArtist(scope.ServiceProvider.GetRequiredService<IAccountStore>()).ExecuteAsync(link.Issuer, link.Subject, CancellationToken.None));
        var roles = (await client.GetFromJsonAsync<JsonObject>("/api/v1/profiles/me"))!["roles"]!.AsArray().Select(role => role!.GetValue<string>());
        Assert.Equal(new[] { "Client", "Artist" }, roles);
    }

    [Fact]
    public async Task Concurrent_first_access_and_distinct_issuers_preserve_unique_links()
    {
        using var identity = new SecurityIdentityHost(fixture);
        var subject = "controlled-" + Guid.NewGuid();
        var attempts = Enumerable.Range(0, 6).Select(async _ =>
        {
            using var scope = identity.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<ProvisionAccount>()
                .ExecuteAsync("https://issuer.one.test", subject, "Concurrent", CancellationToken.None);
        });
        var results = await Task.WhenAll(attempts);
        Assert.Single(results.Select(result => result.ProfileId).Distinct());
        using var otherScope = identity.Services.CreateScope();
        var other = await otherScope.ServiceProvider.GetRequiredService<ProvisionAccount>()
            .ExecuteAsync("https://issuer.two.test", subject, "Other issuer", CancellationToken.None);
        Assert.NotEqual(results[0].ProfileId, other.ProfileId);
    }

    [Fact]
    public async Task Openapi_declares_bearer_security_for_private_operations()
    {
        using var identity = new SecurityIdentityHost(fixture);
        using var client = identity.CreateClient();
        var document = (await client.GetFromJsonAsync<JsonObject>("/openapi/v1.json"))!;
        Assert.Equal("bearer", document["components"]!["securitySchemes"]!["Bearer"]!["scheme"]!.GetValue<string>());
        Assert.NotNull(document["paths"]!["/api/v1/profiles/me"]!["get"]!["security"]);
        Assert.NotNull(document["paths"]!["/api/v1/profiles/me"]!["put"]!["security"]);
        Assert.Null(document["paths"]!["/health/live"]!["get"]!["security"]);
        foreach (var (route, method) in new[] { ("/api/v1/profiles/me", "get"), ("/api/v1/profiles/me", "put"), ("/api/v1/internal/profiles/{id}", "get") })
        {
            var error = document["paths"]![route]![method]!["responses"]!["500"];
            Assert.NotNull(error?["content"]?["application/problem+json"]?["schema"]);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Service_identity_is_scoped_and_never_substitutes_a_user(bool throughGateway)
    {
        using var identity = new SecurityIdentityHost(fixture);
        using var gateway = new SecurityGatewayHost(fixture, identity.Server.CreateHandler());
        using var client = throughGateway ? gateway.CreateClient() : identity.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", (await fixture.TokenAsync())["access_token"]!.GetValue<string>());
        var profile = (await client.GetFromJsonAsync<JsonObject>("/api/v1/profiles/me"))!;
        var path = "/api/v1/internal/profiles/" + profile["id"]!.GetValue<Guid>();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", (await fixture.TokenAsync(service: true, scoped: false))["access_token"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", (await fixture.TokenAsync(service: true))["access_token"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/profiles/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync("/api/v1/profiles/me", new { displayName = "Forbidden" })).StatusCode);
    }
}
