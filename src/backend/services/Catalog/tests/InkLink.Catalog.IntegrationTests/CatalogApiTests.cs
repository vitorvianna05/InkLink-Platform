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


    [Fact]
    public async Task Search_combines_filters_orders_relevance_and_binds_cursors()
    {
        using var scenario = await fixture.CreateAsync();
        var first = await scenario.PublishAsync("alice", "Blackwork", "São Paulo", 300);
        var second = await scenario.PublishAsync("bob", "Blackwork Studio", "São Paulo", 450);
        var page = (await scenario.Proxied.GetFromJsonAsync<JsonObject>("/api/v1/artists/search?term=blakwork&style=BLACKWORK&city=sao%20paulo&maxPrice=450&limit=1"))!;
        Assert.Equal(2, page["total"]!.GetValue<int>());
        Assert.Single(page["items"]!.AsArray()); Assert.True(page["hasMore"]!.GetValue<bool>());
        var cursor = Uri.EscapeDataString(page["nextCursor"]!.GetValue<string>());
        var next = (await scenario.Http.GetFromJsonAsync<JsonObject>("/api/v1/artists/search?term=blakwork&style=BLACKWORK&city=sao%20paulo&maxPrice=450&limit=1&cursor=" + cursor))!;
        Assert.NotEqual(page["items"]![0]!["id"]!.GetValue<Guid>(), next["items"]![0]!["id"]!.GetValue<Guid>());
        Assert.False(next["hasMore"]!.GetValue<bool>());
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.Http.GetAsync("/api/v1/artists/search?term=other&cursor=" + cursor)).StatusCode);
        var budget = (await scenario.Http.GetFromJsonAsync<JsonObject>("/api/v1/artists/search?style=Blackwork&city=sao%20paulo&maxPrice=300"))!;
        Assert.Equal(first, budget["items"]![0]!["id"]!.GetValue<Guid>()); Assert.Equal(1, budget["total"]!.GetValue<int>());
        var empty = (await scenario.Http.GetFromJsonAsync<JsonObject>("/api/v1/artists/search?city=Rio&maxPrice=100"))!;
        Assert.Empty(empty["items"]!.AsArray());
        var relevance = (await scenario.Http.GetFromJsonAsync<JsonObject>("/api/v1/artists/search?term=Blackwork"))!;
        Assert.Equal(first, relevance["items"]![0]!["id"]!.GetValue<Guid>());
        Assert.Contains(second.ToString(), relevance.ToJsonString());
        foreach (var invalid in new[] { "limit=0", "limit=101", "maxPrice=-1", "cursor=invalid", "term=" + new string('x', 101) })
            Assert.Equal(HttpStatusCode.BadRequest, (await scenario.Http.GetAsync("/api/v1/artists/search?" + invalid)).StatusCode);
    }


    [Fact]
    public async Task Search_projection_can_be_rebuilt_from_the_official_catalog()
    {
        using var scenario = await fixture.CreateAsync();
        await scenario.PublishAsync();
        await scenario.PublishAsync("bob", "Camila");
        var options = new DbContextOptionsBuilder<CatalogDbContext>();
        InkLink.Catalog.Infrastructure.DependencyInjection.Configure(options, scenario.OwnerConnection);
        await using (var owner = new CatalogDbContext(options.Options))
        {
            await owner.Database.ExecuteSqlRawAsync("DELETE FROM catalog.search_terms");
            Assert.Empty((await scenario.Http.GetFromJsonAsync<JsonArray>("/api/v1/artists/suggestions?term=rafa"))!);
            Assert.Equal(2, await new CatalogSearchMaintenance(owner).RebuildAsync(default));
            Assert.Equal(2, await new CatalogSearchMaintenance(owner).RebuildAsync(default));
        }
        Assert.NotEmpty((await scenario.Http.GetFromJsonAsync<JsonArray>("/api/v1/artists/suggestions?term=rafa"))!);
        Assert.Equal(2, (await scenario.Http.GetFromJsonAsync<JsonObject>("/api/v1/artists"))!["total"]!.GetValue<int>());
    }


    [Fact]
    public async Task Suggestions_cover_prefix_accents_typo_categories_deduplication_and_update()
    {
        using var scenario = await fixture.CreateAsync();
        await scenario.PublishAsync("alice");
        await scenario.PublishAsync("bob", "Camila", studio: "São Paulo");
        Assert.Empty((await scenario.Http.GetFromJsonAsync<JsonArray>("/api/v1/artists/suggestions"))!);
        Assert.Empty((await scenario.Http.GetFromJsonAsync<JsonArray>("/api/v1/artists/suggestions?term=zzzzzzzzzz"))!);
        var cities = (await scenario.Http.GetFromJsonAsync<JsonArray>("/api/v1/artists/suggestions?term=sao"))!;
        Assert.Single(cities, value => value!["category"]!.GetValue<string>() == "city");
        Assert.Contains(cities, value => value!["category"]!.GetValue<string>() == "studio");
        var typo = (await scenario.Http.GetFromJsonAsync<JsonArray>("/api/v1/artists/suggestions?term=blakwork"))!;
        Assert.Single(typo, value => value!["value"]!.GetValue<string>() == "Blackwork");
        var name = (await scenario.Http.GetFromJsonAsync<JsonArray>("/api/v1/artists/suggestions?term=rafa"))!;
        Assert.Contains(name, value => value!["category"]!.GetValue<string>() == "artist");
        await scenario.PublishAsync("alice", "Nome novo", "Curitiba", styles: ["Realismo"], studio: "Novo estúdio");
        Assert.Empty((await scenario.Http.GetFromJsonAsync<JsonArray>("/api/v1/artists/suggestions?term=rafa"))!);
        var fresh = (await scenario.Http.GetFromJsonAsync<JsonArray>("/api/v1/artists/suggestions?term=curi"))!;
        Assert.Contains(fresh, value => value!["category"]!.GetValue<string>() == "city");
    }


    [Fact]
    public async Task Overlapping_profile_updates_keep_source_and_search_projection_consistent()
    {
        using var scenario = await fixture.CreateAsync();
        var artistId = await scenario.PublishAsync();
        using var scopeA = scenario.Host.Services.CreateScope();
        using var scopeB = scenario.Host.Services.CreateScope();
        var ownerId = await scopeA.ServiceProvider.GetRequiredService<CatalogDbContext>().Artists.Where(artist => artist.Id == artistId).Select(artist => artist.OwnerId).SingleAsync();
        var storeA = scopeA.ServiceProvider.GetRequiredService<IArtistStore>();
        var storeB = scopeB.ServiceProvider.GetRequiredService<IArtistStore>();
        var a = (await storeA.FindByOwnerAsync(ownerId, default))!;
        var b = (await storeB.FindByOwnerAsync(ownerId, default))!;
        a.Update("Nome A", "Estúdio A", "Recife", "Biografia A", ["Aquarela"], 500);
        b.Update("Nome B", b.Studio, b.City, "Biografia B", b.Styles, b.StartingPrice);
        await storeA.SaveArtistAsync(a, false, default);
        await storeB.SaveArtistAsync(b, false, default);
        var current = (await scenario.Http.GetFromJsonAsync<JsonObject>("/api/v1/artists/" + artistId))!;
        Assert.Equal("São Paulo", current["city"]!.GetValue<string>());
        Assert.Equal(300, current["startingPrice"]!.GetValue<decimal>());
        Assert.Empty((await scenario.Http.GetFromJsonAsync<JsonArray>("/api/v1/artists/suggestions?term=recife"))!);
        Assert.NotEmpty((await scenario.Http.GetFromJsonAsync<JsonArray>("/api/v1/artists/suggestions?term=sao"))!);
    }

}
