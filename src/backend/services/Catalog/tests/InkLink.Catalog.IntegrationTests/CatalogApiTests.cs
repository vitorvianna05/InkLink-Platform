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

}
