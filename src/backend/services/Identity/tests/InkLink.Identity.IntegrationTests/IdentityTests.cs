using System.Net;
using System.Text.Json;
using InkLink.Identity.Application;
using InkLink.Identity.Domain;
using InkLink.Identity.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace InkLink.Identity.IntegrationTests;

public sealed class IdentityTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Migrates_clean_database_and_repeating_migration_is_idempotent()
    {
        await using var database = postgres.CreateOwnerContext();
        await database.Database.MigrateAsync();
        Assert.Single(await database.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await database.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task Runtime_cannot_create_tables_or_access_other_context()
    {
        await using var app = new NpgsqlConnection(postgres.AppConnection);
        await app.OpenAsync();
        var ddl = await Assert.ThrowsAsync<PostgresException>(() => PostgresFixture.Execute(app, "CREATE TABLE identity.forbidden (id uuid)"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, ddl.SqlState);
        var other = new NpgsqlConnectionStringBuilder(postgres.AppConnection) { Database = "other_context" };
        await using var foreign = new NpgsqlConnection(other.ConnectionString);
        var access = await Assert.ThrowsAsync<PostgresException>(() => foreign.OpenAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, access.SqlState);
    }

    [Fact]
    public async Task Persists_profile_and_queries_only_the_trusted_current_user()
    {
        var julia = Profile.Create("Júlia Vieira");
        var other = Profile.Create("Outro cliente");
        await using (var database = postgres.CreateOwnerContext())
        {
            database.Profiles.AddRange(julia, other);
            await database.SaveChangesAsync();
        }
        using var host = new IdentityHost(postgres.AppConnection, julia.Id);
        using var scope = host.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<GetMyProfile>().ExecuteAsync();
        Assert.Equal(new ProfileResponse(julia.Id, "Júlia Vieira"), result.Profile);
        using var client = host.CreateClient();
        var response = await client.GetAsync("/api/v1/profiles/" + other.Id);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/profiles/me")).StatusCode);
    }

    [Fact]
    public async Task Unknown_and_missing_current_user_are_not_returned_as_existing_profiles()
    {
        using var unknown = new IdentityHost(postgres.AppConnection, Guid.CreateVersion7());
        using var missing = new IdentityHost(postgres.AppConnection);
        using var unknownScope = unknown.Services.CreateScope();
        using var missingScope = missing.Services.CreateScope();
        Assert.Equal(ProfileStatus.NotFound, (await unknownScope.ServiceProvider.GetRequiredService<GetMyProfile>().ExecuteAsync()).Status);
        Assert.Equal(ProfileStatus.Unauthenticated, (await missingScope.ServiceProvider.GetRequiredService<GetMyProfile>().ExecuteAsync()).Status);
    }

    [Fact]
    public async Task Database_also_rejects_invalid_profile_name()
    {
        await using var app = new NpgsqlConnection(postgres.AppConnection);
        await app.OpenAsync();
        var error = await Assert.ThrowsAsync<PostgresException>(() => PostgresFixture.Execute(app,
            $"INSERT INTO identity.profiles (id, display_name) VALUES ('{Guid.CreateVersion7()}', ' ')"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    [Fact]
    public async Task Healthy_endpoints_and_openapi_are_available()
    {
        using var host = new IdentityHost(postgres.AppConnection);
        using var client = host.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/openapi/v1.json")).StatusCode);
    }

    [Fact]
    public async Task Startup_does_not_apply_migrations_and_unmigrated_database_is_not_ready()
    {
        using var host = new IdentityHost(postgres.UnmigratedConnection);
        using var client = host.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        var response = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        await AssertProblem(response, "identity.database_unavailable");
        await using var connection = new NpgsqlConnection(postgres.UnmigratedConnection);
        await connection.OpenAsync();
        await using var query = new NpgsqlCommand("SELECT to_regclass('identity.profiles')::text", connection);
        Assert.Equal(DBNull.Value, await query.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Unavailable_database_is_not_confused_with_dead_process()
    {
        using var host = new IdentityHost("Host=127.0.0.1;Port=1;Database=missing;Username=app;Password=secret-test-password;Timeout=1");
        using var client = host.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        var response = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        await AssertProblem(response, "identity.database_unavailable");
    }

    [Theory]
    [InlineData("Password=secret-test-password;InvalidSetting=broken")]
    [InlineData("Host=localhost;Password=secret-test-password")]
    public void Invalid_configuration_is_rejected_without_leaking_its_value(string connection)
    {
        using var host = new IdentityHost(connection);
        var error = Assert.ThrowsAny<Exception>(() => host.CreateClient());
        Assert.Contains("database configuration", error.ToString());
        Assert.DoesNotContain("secret-test-password", error.ToString());
    }

    [Fact]
    public void Missing_configuration_fails_startup_without_exposing_credentials()
    {
        using var host = new IdentityHost("");
        var error = Assert.ThrowsAny<Exception>(() => host.CreateClient());
        Assert.Contains("ConnectionString is required", error.ToString());
        Assert.DoesNotContain("Password=", error.ToString());
    }

    [Fact]
    public async Task Unexpected_errors_are_sanitized_and_correlated()
    {
        using var host = new IdentityHost(postgres.AppConnection, failRoute: true);
        using var client = host.CreateClient();
        var response = await client.GetAsync("/__test/failure");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        await AssertProblem(response, "internal_error");
    }

    [Theory]
    [InlineData("application/json")]
    [InlineData("text/plain")]
    [InlineData("text/html")]
    [InlineData("application/xml")]
    public async Task Missing_route_always_returns_the_problem_contract(string accept)
    {
        using var host = new IdentityHost(postgres.AppConnection);
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Accept.ParseAdd(accept);
        var response = await client.GetAsync("/does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertProblem(response, "http.404");
    }

    [Theory]
    [InlineData("application/json")]
    [InlineData("text/plain")]
    [InlineData("text/html")]
    [InlineData("application/xml")]
    public async Task Unexpected_failure_always_returns_a_sanitized_problem(string accept)
    {
        using var host = new IdentityHost(postgres.AppConnection, failRoute: true);
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Accept.ParseAdd(accept);
        var response = await client.GetAsync("/__test/failure");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        await AssertProblem(response, "internal_error");
    }

    [Fact]
    public async Task Openapi_describes_health_bodies_and_problem_responses()
    {
        using var host = new IdentityHost(postgres.AppConnection);
        using var client = host.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        foreach (var path in new[] { "/health/live", "/health/ready" })
        {
            var responses = document.RootElement.GetProperty("paths").GetProperty(path).GetProperty("get").GetProperty("responses");
            var health = ResolveSchema(document, responses.GetProperty("200").GetProperty("content")
                .GetProperty("application/json").GetProperty("schema"));
            Assert.Equal("string", health.GetProperty("properties").GetProperty("status").GetProperty("type").GetString());
            AssertProblemSchema(document, responses.GetProperty("500"));
            if (path == "/health/ready") AssertProblemSchema(document, responses.GetProperty("503"));
        }
    }

    private static void AssertProblemSchema(JsonDocument document, JsonElement response)
    {
        var schema = ResolveSchema(document, response.GetProperty("content").GetProperty("application/problem+json").GetProperty("schema"));
        var properties = schema.GetProperty("properties");
        foreach (var name in new[] { "type", "title", "status", "code", "traceId" })
            Assert.True(properties.TryGetProperty(name, out _), "Missing problem property: " + name);
        Assert.Equal("string", properties.GetProperty("code").GetProperty("type").GetString());
        Assert.Equal("string", properties.GetProperty("traceId").GetProperty("type").GetString());
        var required = schema.GetProperty("required").EnumerateArray().Select(item => item.GetString());
        Assert.Contains("code", required);
        Assert.Contains("traceId", required);
    }

    private static JsonElement ResolveSchema(JsonDocument document, JsonElement schema)
    {
        if (!schema.TryGetProperty("$ref", out var reference)) return schema;
        var resolved = document.RootElement;
        foreach (var segment in reference.GetString()![2..].Split('/'))
            resolved = resolved.GetProperty(segment);
        return resolved;
    }

    private static async Task AssertProblem(HttpResponseMessage response, string code)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("secret-test-password", text);
        Assert.DoesNotContain("stackTrace", text, StringComparison.OrdinalIgnoreCase);
        using var json = JsonDocument.Parse(text);
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
        Assert.Equal((int)response.StatusCode, json.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(response.Headers.GetValues("X-Trace-Id").Single(), json.RootElement.GetProperty("traceId").GetString());
    }
}
