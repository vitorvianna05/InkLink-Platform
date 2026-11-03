using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using InkLink.Catalog.Application.Publishing;
using InkLink.Catalog.Infrastructure.Persistence;
using InkLink.Identity.IntegrationTests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace InkLink.Catalog.IntegrationTests;

[Collection("catalog")]
public sealed class CatalogApiTests(CatalogFixture fixture)
{
    [Fact]
    public async Task Media_are_distinct_owned_validated_and_have_a_resolvable_location()
    {
        using var scenario = await fixture.CreateAsync();
        var artist = await scenario.PublishAsync();
        var media = new { title = "Rosa", imageUrl = "https://images.example.test/rosa.jpg", alternativeText = "Desenho de uma rosa", style = "Blackwork", referencePrice = 450m };
        var flash = await scenario.Http.PostAsJsonAsync("/api/v1/artists/me/flashes", media);
        Assert.Equal(HttpStatusCode.Created, flash.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await scenario.Http.GetAsync(flash.Headers.Location)).StatusCode);
        var id = (await flash.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<Guid>();
        var tattoos = (await scenario.Http.GetFromJsonAsync<JsonObject>($"/api/v1/artists/{artist}/tattoos"))!;
        Assert.Empty(tattoos["items"]!.AsArray());
        var flashes = (await scenario.Http.GetFromJsonAsync<JsonObject>($"/api/v1/artists/{artist}/flashes"))!;
        Assert.Single(flashes["items"]!.AsArray());
        Assert.Equal(HttpStatusCode.NotFound, (await scenario.Http.GetAsync($"/api/v1/artists/{Guid.NewGuid()}/flashes/{id}")).StatusCode);
        await scenario.PublishAsync("bob", "Outro artista");
        Assert.Equal(HttpStatusCode.NotFound, (await scenario.Http.PutAsJsonAsync($"/api/v1/artists/me/flashes/{id}", media)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await scenario.Http.DeleteAsync($"/api/v1/artists/me/flashes/{id}")).StatusCode);
        scenario.SignIn("alice");
        var invalid = await scenario.Http.PostAsJsonAsync("/api/v1/artists/me/flashes", new { media.title, imageUrl = "http://images.example.test/a.jpg", media.alternativeText, media.style, media.referencePrice });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var problem = (await invalid.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.NotNull(problem["errors"]?["imageUrl"]); Assert.NotNull(problem["traceId"]); Assert.NotNull(problem["code"]);
        Assert.Equal(HttpStatusCode.NoContent, (await scenario.Http.PutAsJsonAsync($"/api/v1/artists/me/flashes/{id}", media)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await scenario.Http.DeleteAsync($"/api/v1/artists/me/flashes/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await scenario.Http.GetAsync(flash.Headers.Location)).StatusCode);
    }


    [Fact]
    public async Task Runtime_credential_cannot_create_tables_and_readiness_detects_missing_schema()
    {
        using var scenario = await fixture.CreateAsync();
        await using var connection = new NpgsqlConnection(scenario.AppConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("CREATE TABLE catalog.forbidden(id int)", connection);
        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
        var otherContext = new NpgsqlConnectionStringBuilder(scenario.AppConnection) { Database = "catalog_other_context" };
        await using (var other = new NpgsqlConnection(otherContext.ConnectionString))
        {
            var denied = await Assert.ThrowsAsync<PostgresException>(() => other.OpenAsync());
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, denied.SqlState);
        }
        // Catalog has no Identity schema or cross-service table linkage.
        await using var privateQuery = new NpgsqlCommand("SELECT * FROM identity.profiles", connection);
        await Assert.ThrowsAsync<PostgresException>(() => privateQuery.ExecuteNonQueryAsync());
        using var unmigrated = await fixture.CreateAsync(migrate: false);
        var notReady = await unmigrated.Http.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, notReady.StatusCode);
        Assert.Equal("catalog.database_unavailable", (await notReady.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>());
    }


    [Fact]
    public async Task Empty_catalog_and_health_are_explicit_and_public_through_all_hosts()
    {
        using var scenario = await fixture.CreateAsync();
        using var bff = new SecurityBffHost(fixture.Security, scenario.Gateway.Server.CreateHandler());
        using var web = bff.CreateClient(new() { BaseAddress = new Uri("https://localhost:5192") });
        foreach (var http in new[] { scenario.Http, scenario.Proxied, web })
        {
            var page = (await http.GetFromJsonAsync<JsonObject>("/api/v1/artists"))!;
            Assert.Empty(page["items"]!.AsArray()); Assert.Equal(0, page["total"]!.GetValue<int>());
            Assert.False(page["hasMore"]!.GetValue<bool>()); Assert.Null(page["nextCursor"]);
        }
        Assert.Equal(HttpStatusCode.OK, (await scenario.Http.GetAsync("/health/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await scenario.Http.GetAsync("/api/v1/artists/" + Guid.NewGuid())).StatusCode);
    }


    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Publication_requires_a_real_artist_and_cannot_assign_an_owner(bool throughGateway)
    {
        using var scenario = await fixture.CreateAsync();
        var http = throughGateway ? scenario.Proxied : scenario.Http;
        var input = new { name = "Artista", studio = "Estúdio", city = "São Paulo", biography = "Bio", styles = new[] { "Blackwork" }, startingPrice = 200 };
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PutAsJsonAsync("/api/v1/artists/me", input)).StatusCode);
        scenario.SignIn("unprovisioned", throughGateway);
        http.DefaultRequestHeaders.Add("X-Roles", "Artist");
        http.DefaultRequestHeaders.Add("X-User-Id", Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Forbidden, (await http.PutAsJsonAsync("/api/v1/artists/me", input)).StatusCode);
        scenario.SignIn("alice", throughGateway);
        var forged = await http.PutAsJsonAsync("/api/v1/artists/me", new { input.name, input.studio, input.city, input.biography, input.styles, input.startingPrice, ownerId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
        var created = await http.PutAsJsonAsync("/api/v1/artists/me/", input);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(created.Headers.Location)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.PutAsJsonAsync("/api/v1/artists/me", input)).StatusCode);
        var page = (await http.GetFromJsonAsync<JsonObject>("/api/v1/artists"))!;
        Assert.Single(page["items"]!.AsArray());
        Assert.DoesNotContain("ownerId", await created.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public async Task Discovery_uses_stable_name_and_identifier_order_without_duplicates()
    {
        using var scenario = await fixture.CreateAsync();
        var bob = await scenario.PublishAsync("bob", "Zeca");
        var alice = await scenario.PublishAsync("alice", "Ana");
        var first = (await scenario.Http.GetFromJsonAsync<JsonObject>("/api/v1/artists?limit=1"))!;
        Assert.Equal(alice, first["items"]![0]!["id"]!.GetValue<Guid>());
        var second = (await scenario.Http.GetFromJsonAsync<JsonObject>("/api/v1/artists?limit=1&cursor=" + Uri.EscapeDataString(first["nextCursor"]!.GetValue<string>())))!;
        Assert.Equal(bob, second["items"]![0]!["id"]!.GetValue<Guid>());
        Assert.False(second["hasMore"]!.GetValue<bool>());
    }


    [Fact]
    public async Task Concurrent_first_publication_never_duplicates_a_profile()
    {
        using var scenario = await fixture.CreateAsync();
        scenario.SignIn("alice");
        var input = new { name = "Artista", studio = "Estúdio", city = "São Paulo", biography = "Bio", styles = new[] { "Blackwork" }, startingPrice = 100 };
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => scenario.Http.PutAsJsonAsync("/api/v1/artists/me", input)));
        Assert.Single(results, result => result.StatusCode == HttpStatusCode.Created);
        Assert.All(results, result => Assert.Contains(result.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK, HttpStatusCode.Conflict }));
        Assert.Equal(1, (await scenario.Http.GetFromJsonAsync<JsonObject>("/api/v1/artists"))!["total"]!.GetValue<int>());
    }


    [Fact]
    public async Task Identity_outage_fails_private_operations_closed_and_keeps_public_reads_available()
    {
        using var scenario = await fixture.CreateAsync();
        using var unavailable = new CatalogHost(fixture, scenario.AppConnection, identityUnavailable: true);
        using var http = unavailable.CreateClient();
        http.DefaultRequestHeaders.Authorization = new("Bearer", fixture.Tokens["alice"]);
        var privateResponse = await http.GetAsync("/api/v1/favorites");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, privateResponse.StatusCode);
        Assert.Equal("catalog.identity_unavailable", (await privateResponse.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/api/v1/artists")).StatusCode);
    }


    [Fact]
    public async Task Required_price_and_invalid_publication_do_not_create_catalog_data()
    {
        using var scenario = await fixture.CreateAsync();
        scenario.SignIn("alice");
        var input = new { name = "A", studio = "B", city = "C", biography = "D", styles = new[] { "Blackwork" } };
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.Http.PutAsJsonAsync("/api/v1/artists/me", input)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.Http.PutAsJsonAsync("/api/v1/artists/me", new { input.name, input.studio, input.city, input.biography, styles = new[] { "Blackwork", "BLACKWORK" }, startingPrice = 100 })).StatusCode);
        Assert.Empty((await scenario.Http.GetFromJsonAsync<JsonObject>("/api/v1/artists"))!["items"]!.AsArray());
    }

}
