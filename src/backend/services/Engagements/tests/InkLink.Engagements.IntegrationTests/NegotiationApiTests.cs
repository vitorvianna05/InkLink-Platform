using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using InkLink.Engagements.Application.Negotiations;
using InkLink.Engagements.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace InkLink.Engagements.IntegrationTests;

[Collection("engagements")]
public sealed class NegotiationApiTests(EngagementFixture fixture)
{
    private HttpClient Client(string? user = "alice")
    {
        var http = fixture.Engagements.CreateClient();
        if (user is not null) http.DefaultRequestHeaders.Authorization = new("Bearer", fixture.Token(user));
        return http;
    }

    private async Task<Guid> ArtistAsync(string user = "bob")
    {
        using var http = fixture.Catalog.CreateClient(); http.DefaultRequestHeaders.Authorization = new("Bearer", fixture.Token(user));
        var response = await http.PutAsJsonAsync("/api/v1/artists/me", new { name = "Artista " + user, studio = "Estúdio", city = "São Paulo", biography = "Portfólio", styles = new[] { "Blackwork" }, startingPrice = 300 });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<Guid>();
    }

    private async Task<EngagementView> RequestAsync(HttpClient http, Guid? artist = null, Guid? flash = null, Guid? key = null)
    {
        var input = new RequestInput(artist ?? await ArtistAsync(), "Ideia autoral", "Braço", 12, flash);
        using var response = await PostAsync(http, "/api/v2/engagements", input, key ?? Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<EngagementView>())!;
    }

    private static Task<HttpResponseMessage> PostAsync<T>(HttpClient http, string path, T input, Guid key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(input) };
        request.Headers.Add("Idempotency-Key", key.ToString());
        return http.SendAsync(request);
    }

    private static async Task<ProposalView> ProposeAsync(HttpClient http, Guid engagement, Guid? key = null, decimal amount = 500)
    {
        using var response = await PostAsync(http, $"/api/v2/engagements/{engagement}/proposals", new ProposalInput("Sessão de tatuagem", amount), key ?? Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProposalView>())!;
    }

    [Fact]
    public async Task Complete_flow_persists_for_both_participants_after_new_host()
    {
        using var client = Client(); using var artist = Client("bob");
        var e = await RequestAsync(client); var p = await ProposeAsync(artist, e.Id);
        var path = $"/api/v2/engagements/{e.Id}/proposals/{p.Id}/acceptance";
        var accepted = await client.PutAsync(path, null); Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var first = await accepted.Content.ReadFromJsonAsync<ProposalView>();
        var again = await (await client.PutAsync(path, null)).Content.ReadFromJsonAsync<ProposalView>();
        Assert.Equal(first, again);
        using var host = fixture.NewHost(); using var fresh = host.CreateClient(); fresh.DefaultRequestHeaders.Authorization = new("Bearer", fixture.Token("bob"));
        var result = await fresh.GetFromJsonAsync<EngagementView>($"/api/v2/engagements/{e.Id}");
        Assert.Equal("Agreed", result!.State); Assert.Equal("Accepted", result.CurrentProposal!.State);
        Assert.Equal("BRL", result.CurrentProposal.Currency); Assert.Equal(e.Catalog, result.Catalog);
        var location = await artist.GetAsync($"/api/v2/engagements/{e.Id}/proposals/{p.Id}"); Assert.Equal(HttpStatusCode.OK, location.StatusCode);
        await using var database = fixture.CreateOwnerContext();
        Assert.Equal(1, await database.Proposals.CountAsync(x => x.EngagementId == e.Id));
        Assert.Equal(new[] { "engagements", "proposals", "__ef_migrations_history" }.Order(), database.Model.GetEntityTypes().Select(t => t.GetTableName()!).Append("__ef_migrations_history").Order());
    }

    [Fact]
    public async Task Replacement_invalidates_old_proposal_and_freezes_after_acceptance()
    {
        using var client = Client(); using var artist = Client("bob");
        var e = await RequestAsync(client); var first = await ProposeAsync(artist, e.Id); var second = await ProposeAsync(artist, e.Id, amount: 600);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsync($"/api/v2/engagements/{e.Id}/proposals/{first.Id}/acceptance", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsync($"/api/v2/engagements/{e.Id}/proposals/{second.Id}/acceptance", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(artist, $"/api/v2/engagements/{e.Id}/proposals", new ProposalInput("Outra", 700), Guid.NewGuid())).StatusCode);
        var result = await client.GetFromJsonAsync<EngagementView>($"/api/v2/engagements/{e.Id}");
        var history = await client.GetFromJsonAsync<Page<ProposalView>>($"/api/v2/engagements/{e.Id}/proposals");
        Assert.Equal(new[] { "Superseded", "Accepted" }, history!.Items.Select(p => p.State));
        Assert.Equal(second.Id, result!.CurrentProposal!.Id);
    }

    [Fact]
    public async Task Request_repetition_is_durable_and_different_payload_conflicts()
    {
        using var client = Client(); var artist = await ArtistAsync(); var key = Guid.NewGuid();
        var e = await RequestAsync(client, artist, key: key);
        var input = new RequestInput(artist, "  Ideia autoral  ", "Braço", 12.00m);
        using var unavailable = fixture.NewHost(catalogUnavailable: true); using var retry = unavailable.CreateClient();
        retry.DefaultRequestHeaders.Authorization = client.DefaultRequestHeaders.Authorization;
        var repeated = await PostAsync(retry, "/api/v2/engagements", input, key);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode); Assert.Equal(e.Id, (await repeated.Content.ReadFromJsonAsync<EngagementView>())!.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(client, "/api/v2/engagements", input with { Idea = "Outra" }, key)).StatusCode);
        var concurrentKey = Guid.NewGuid();
        var pair = await Task.WhenAll(PostAsync(client, "/api/v2/engagements", input, concurrentKey), PostAsync(client, "/api/v2/engagements", input, concurrentKey));
        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.Created }, pair.Select(p => p.StatusCode).Order());
        Assert.Equal((await pair[0].Content.ReadFromJsonAsync<EngagementView>())!.Id, (await pair[1].Content.ReadFromJsonAsync<EngagementView>())!.Id);
    }

    [Fact]
    public async Task Repeating_proposal_never_reactivates_superseded_version()
    {
        using var client = Client(); using var artist = Client("bob"); var e = await RequestAsync(client); var key = Guid.NewGuid();
        var first = await ProposeAsync(artist, e.Id, key); var second = await ProposeAsync(artist, e.Id);
        var repeat = await PostAsync(artist, $"/api/v2/engagements/{e.Id}/proposals", new ProposalInput("Sessão de tatuagem", 500), key);
        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode); Assert.Equal("Superseded", (await repeat.Content.ReadFromJsonAsync<ProposalView>())!.State);
        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(artist, $"/api/v2/engagements/{e.Id}/proposals", new ProposalInput("Alteração", 500), key)).StatusCode);
        Assert.Equal(second.Id, (await client.GetFromJsonAsync<EngagementView>($"/api/v2/engagements/{e.Id}"))!.CurrentProposalId);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public async Task Participants_are_derived_from_identity_and_third_parties_cannot_read_or_write()
    {
        using var client = Client(); using var artist = Client("bob"); using var third = Client("carol");
        var e = await RequestAsync(client); var p = await ProposeAsync(artist, e.Id);
        Assert.Equal(fixture.Accounts["alice"], e.ClientId); Assert.Equal(fixture.Accounts["bob"], e.ArtistAccountId);
        Assert.Equal(HttpStatusCode.NotFound, (await third.GetAsync($"/api/v2/engagements/{e.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await third.PutAsync($"/api/v2/engagements/{e.Id}/proposals/{p.Id}/acceptance", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await artist.PutAsync($"/api/v2/engagements/{e.Id}/proposals/{p.Id}/acceptance", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(client, $"/api/v2/engagements/{e.Id}/proposals", new ProposalInput("Forjada", 100), Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(third, $"/api/v2/engagements/{e.Id}/proposals", new ProposalInput("Forjada", 100), Guid.NewGuid())).StatusCode);
        third.DefaultRequestHeaders.Add("X-User-Id", e.ClientId.ToString()); third.DefaultRequestHeaders.Add("X-Roles", "Artist");
        Assert.Equal(HttpStatusCode.NotFound, (await third.GetAsync($"/api/v2/engagements/{e.Id}")).StatusCode);
        var other = await RequestAsync(client);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsync($"/api/v2/engagements/{other.Id}/proposals/{p.Id}/acceptance", null)).StatusCode);
        Assert.Equal("Negotiating", (await client.GetFromJsonAsync<EngagementView>($"/api/v2/engagements/{e.Id}"))!.State);
    }

    [Fact]
    public async Task Flash_snapshot_survives_catalog_change_and_deletion()
    {
        var artistId = await ArtistAsync(); using var catalog = fixture.Catalog.CreateClient(); catalog.DefaultRequestHeaders.Authorization = new("Bearer", fixture.Token("bob"));
        var flashResponse = await catalog.PostAsJsonAsync("/api/v1/artists/me/flashes", new { title = "Flor", imageUrl = "https://images.example.test/flower.png", alternativeText = "Flor preta", style = "Blackwork", referencePrice = 500 });
        Assert.Equal(HttpStatusCode.Created, flashResponse.StatusCode);
        var flash = (await flashResponse.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<Guid>();
        using var client = Client(); var key = Guid.NewGuid(); var e = await RequestAsync(client, artistId, flash, key);
        Assert.Equal("Flor", e.Catalog.Flash!.Title);
        Assert.Equal(HttpStatusCode.NoContent, (await catalog.DeleteAsync($"/api/v1/artists/me/flashes/{flash}")).StatusCode);
        await catalog.PutAsJsonAsync("/api/v1/artists/me", new { name = "Nome novo", studio = "Outro estúdio", city = "Rio", biography = "Nova", styles = new[] { "Fine line" }, startingPrice = 900 });
        var persisted = await client.GetFromJsonAsync<EngagementView>($"/api/v2/engagements/{e.Id}"); Assert.Equal(e.Catalog, persisted!.Catalog);
        var repeated = await PostAsync(client, "/api/v2/engagements", new RequestInput(artistId, "Ideia autoral", "Braço", 12, flash), key);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(client, "/api/v2/engagements", new RequestInput(artistId, "Ideia", "Braço", 12, flash), Guid.NewGuid())).StatusCode);
    }

    [Fact]
    public async Task Invalid_requests_do_not_persist_and_return_correlated_problems()
    {
        using var client = Client(); var artist = await ArtistAsync();
        var invalid = new[] { new RequestInput(artist, " ", "Braço", 12), new RequestInput(artist, "Ideia", "", 12), new RequestInput(artist, "Ideia", "Braço", 0), new RequestInput(Guid.Empty, "Ideia", "Braço", 12), new RequestInput(artist, "Ideia", "Braço", 1.234m) };
        await using var database = fixture.CreateOwnerContext(); var before = await database.Engagements.CountAsync();
        foreach (var input in invalid)
        {
            var response = await PostAsync(client, "/api/v2/engagements", input, Guid.NewGuid()); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<JsonObject>(); Assert.NotNull(problem!["errors"]); Assert.NotNull(problem["code"]); Assert.NotNull(problem["traceId"]);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        }
        var valid = new RequestInput(artist, "Ideia", "Braço", 12);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v2/engagements", valid)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(client, "/api/v2/engagements", new { artistId = artist, idea = "Ideia", bodyRegion = "Braço", approximateSizeCm = 12, clientId = Guid.NewGuid() }, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(client, "/api/v2/engagements", valid with { ArtistId = await ArtistAsync("alice") }, Guid.NewGuid())).StatusCode);
        Assert.Equal(before, await database.Engagements.CountAsync());
    }

    [Fact]
    public async Task Concurrent_acceptance_and_replacement_have_a_consistent_order()
    {
        using var client = Client(); using var artist = Client("bob"); var e = await RequestAsync(client); var p = await ProposeAsync(artist, e.Id);
        var results = await Task.WhenAll(client.PutAsync($"/api/v2/engagements/{e.Id}/proposals/{p.Id}/acceptance", null), PostAsync(artist, $"/api/v2/engagements/{e.Id}/proposals", new ProposalInput("Nova", 800), Guid.NewGuid()));
        var current = (await client.GetFromJsonAsync<EngagementView>($"/api/v2/engagements/{e.Id}"))!;
        if (results[0].StatusCode == HttpStatusCode.OK)
        {
            Assert.Equal(HttpStatusCode.Conflict, results[1].StatusCode); Assert.Equal("Agreed", current.State); Assert.Equal(p.Id, current.CurrentProposal!.Id);
        }
        else
        {
            Assert.Equal(HttpStatusCode.Conflict, results[0].StatusCode); Assert.Equal(HttpStatusCode.Created, results[1].StatusCode);
            Assert.Equal("Negotiating", current.State);
            var history = (await client.GetFromJsonAsync<Page<ProposalView>>($"/api/v2/engagements/{e.Id}/proposals"))!;
            Assert.Equal(2, history.Total); Assert.Equal("Superseded", history.Items.Single(x => x.Id == p.Id).State);
        }
    }

    [Fact]
    public async Task Concurrent_proposals_and_acceptances_are_idempotent()
    {
        using var client = Client(); using var artist = Client("bob"); var e = await RequestAsync(client); var key = Guid.NewGuid();
        var proposals = await Task.WhenAll(PostAsync(artist, $"/api/v2/engagements/{e.Id}/proposals", new ProposalInput("Uma", 400), key), PostAsync(artist, $"/api/v2/engagements/{e.Id}/proposals", new ProposalInput("Uma", 400), key));
        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.Created }, proposals.Select(p => p.StatusCode).Order());
        var p = (await proposals[0].Content.ReadFromJsonAsync<ProposalView>())!;
        var results = await Task.WhenAll(client.PutAsync($"/api/v2/engagements/{e.Id}/proposals/{p.Id}/acceptance", null), client.PutAsync($"/api/v2/engagements/{e.Id}/proposals/{p.Id}/acceptance", null));
        Assert.All(results, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        Assert.Equal(3, (await client.GetFromJsonAsync<EngagementView>($"/api/v2/engagements/{e.Id}"))!.Revision);
    }

    [Fact]
    public async Task Listing_is_paginated_and_cursors_are_scoped_to_account_and_perspective()
    {
        using var client = Client(); using var artist = Client("bob"); using var third = Client("carol");
        await RequestAsync(client); await RequestAsync(client);
        var page = (await client.GetFromJsonAsync<Page<EngagementView>>("/api/v2/engagements?limit=1"))!;
        Assert.Single(page.Items); Assert.True(page.HasMore); Assert.True(page.Total >= 2);
        var cursor = Uri.EscapeDataString(page.NextCursor!);
        var next = (await client.GetFromJsonAsync<Page<EngagementView>>("/api/v2/engagements?limit=1&cursor=" + cursor))!;
        Assert.NotEqual(page.Items[0].Id, next.Items[0].Id); Assert.Equal(page.Total, next.Total);
        Assert.Equal(HttpStatusCode.BadRequest, (await artist.GetAsync("/api/v2/engagements?perspective=artist&cursor=" + cursor)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await third.GetAsync("/api/v2/engagements?cursor=" + cursor)).StatusCode);
        Assert.Empty((await third.GetFromJsonAsync<Page<EngagementView>>("/api/v2/engagements"))!.Items);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v2/engagements?limit=101")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await third.GetAsync("/api/v2/engagements?perspective=artist")).StatusCode);
        Assert.All((await artist.GetFromJsonAsync<Page<EngagementView>>("/api/v2/engagements?perspective=artist"))!.Items, e => Assert.Equal(fixture.Accounts["bob"], e.ArtistAccountId));
    }

    [Fact]
    public async Task Dependencies_fail_closed_and_database_permissions_are_private()
    {
        using var client = Client(); var artist = await ArtistAsync(); var input = new RequestInput(artist, "Ideia", "Braço", 12);
        foreach (var identityFailure in new[] { true, false })
        {
            using var host = fixture.NewHost(identityUnavailable: identityFailure, catalogUnavailable: !identityFailure); using var http = host.CreateClient();
            http.DefaultRequestHeaders.Authorization = client.DefaultRequestHeaders.Authorization;
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await PostAsync(http, "/api/v2/engagements", input, Guid.NewGuid())).StatusCode);
        }
        await using var connection = new NpgsqlConnection(fixture.AppConnection); await connection.OpenAsync();
        await using var command = new NpgsqlCommand("CREATE TABLE engagements.forbidden(id uuid)", connection);
        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync()); Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
        await using var otherDatabase = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(fixture.AppConnection) { Database = fixture.CatalogDatabaseName }.ConnectionString);
        var denied = await Assert.ThrowsAsync<PostgresException>(() => otherDatabase.OpenAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, denied.SqlState);
    }

    [Fact]
    public async Task Flash_from_other_artist_or_tattoo_is_rejected_without_creating_request()
    {
        var bob = await ArtistAsync(); var alice = await ArtistAsync("alice");
        using var publication = fixture.Catalog.CreateClient(); publication.DefaultRequestHeaders.Authorization = new("Bearer", fixture.Token("alice"));
        var response = await publication.PostAsJsonAsync("/api/v1/artists/me/flashes", new { title = "Outro", imageUrl = "https://images.example.test/other.png", alternativeText = "Outro flash", style = "Blackwork" });
        var flash = (await response.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<Guid>();
        using var client = Client("carol");
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(client, "/api/v2/engagements", new RequestInput(bob, "Ideia", "Braço", 12, flash), Guid.NewGuid())).StatusCode);
        var tattoo = await publication.PostAsJsonAsync("/api/v1/artists/me/tattoos", new { title = "Realizada", imageUrl = "https://images.example.test/tattoo.png", alternativeText = "Tatuagem", style = "Blackwork" });
        var tattooId = (await tattoo.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<Guid>();
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(client, "/api/v2/engagements", new RequestInput(alice, "Ideia", "Braço", 12, tattooId), Guid.NewGuid())).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<Page<EngagementView>>("/api/v2/engagements"))!.Items);
    }

    [Fact]
    public async Task Concurrent_different_proposals_preserve_one_pending_version()
    {
        using var client = Client(); using var artist = Client("bob"); var e = await RequestAsync(client);
        var results = await Task.WhenAll(PostAsync(artist, $"/api/v2/engagements/{e.Id}/proposals", new ProposalInput("Primeira", 300), Guid.NewGuid()),
            PostAsync(artist, $"/api/v2/engagements/{e.Id}/proposals", new ProposalInput("Segunda", 400), Guid.NewGuid()));
        Assert.All(results, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        var state = (await client.GetFromJsonAsync<EngagementView>($"/api/v2/engagements/{e.Id}"))!;
        var history = (await client.GetFromJsonAsync<Page<ProposalView>>($"/api/v2/engagements/{e.Id}/proposals"))!;
        Assert.Equal(2, history.Total);
        var pending = Assert.Single(history.Items, p => p.State == "Pending");
        Assert.Equal(pending.Id, state.CurrentProposalId);
        Assert.Single(history.Items, p => p.State == "Superseded");
    }

    [Fact]
    public async Task Tokens_are_validated_by_service_and_gateway_and_internal_contract_is_scoped()
    {
        using var http = Client(null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/v2/engagements")).StatusCode);
        http.DefaultRequestHeaders.Authorization = new("Bearer", fixture.Token(expired: true));
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/v2/engagements")).StatusCode);
        http.DefaultRequestHeaders.Authorization = new("Bearer", fixture.Token("service", "service", "inklink-engagements", "catalog.engagement-context.read"));
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync("/api/v2/engagements")).StatusCode);
        using var gateway = fixture.Gateway(); using var proxied = gateway.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await proxied.GetAsync("/api/v2/engagements")).StatusCode);
        proxied.DefaultRequestHeaders.Authorization = new("Bearer", fixture.Token());
        Assert.Equal(HttpStatusCode.OK, (await proxied.GetAsync("/api/v2/engagements")).StatusCode);
        var artist = await ArtistAsync(); using var catalog = fixture.Catalog.CreateClient();
        var path = $"/api/v1/internal/artists/{artist}/engagement-context";
        catalog.DefaultRequestHeaders.Authorization = new("Bearer", fixture.Token()); Assert.Equal(HttpStatusCode.Forbidden, (await catalog.GetAsync(path)).StatusCode);
        catalog.DefaultRequestHeaders.Authorization = new("Bearer", fixture.Token("service", "service", "inklink-catalog", "catalog.engagement-context.read")); Assert.Equal(HttpStatusCode.Forbidden, (await catalog.GetAsync(path)).StatusCode);
        catalog.DefaultRequestHeaders.Authorization = new("Bearer", fixture.Token("service", "service", "inklink-engagements")); Assert.Equal(HttpStatusCode.Forbidden, (await catalog.GetAsync(path)).StatusCode);
        catalog.DefaultRequestHeaders.Authorization = http.DefaultRequestHeaders.Authorization; Assert.Equal(HttpStatusCode.OK, (await catalog.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await proxied.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task Proposal_history_is_bounded_and_cursors_cannot_cross_accounts_or_engagements()
    {
        using var client = Client(); using var artist = Client("bob"); using var third = Client("carol");
        var e = await RequestAsync(client);
        Guid firstId;
        Guid lastId;
        await using (var database = fixture.CreateOwnerContext())
        {
            var entity = await database.Engagements.SingleAsync(x => x.Id == e.Id);
            InkLink.Engagements.Domain.Proposal? previous = null;
            firstId = Guid.Empty;
            for (var i = 0; i < 105; i++)
            {
                var proposal = InkLink.Engagements.Domain.Proposal.Create(e.Id, Guid.NewGuid(), "fixture", "Versão " + i, 500, DateTimeOffset.UtcNow);
                entity.Present(proposal, previous); database.Proposals.Add(proposal); previous = proposal;
                if (i == 0) firstId = proposal.Id;
            }
            lastId = previous!.Id;
            await database.SaveChangesAsync();
        }
        var path = $"/api/v2/engagements/{e.Id}/proposals";
        var page = (await client.GetFromJsonAsync<Page<ProposalView>>(path))!;
        Assert.Equal(20, page.Items.Count); Assert.Equal(105, page.Total); Assert.True(page.HasMore);
        var ids = new List<Guid>();
        string? cursor = null;
        do
        {
            var response = (await client.GetFromJsonAsync<Page<ProposalView>>(path + "?limit=25" + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor))))!;
            Assert.InRange(response.Items.Count, 1, 25); Assert.Equal(105, response.Total);
            ids.AddRange(response.Items.Select(p => p.Id)); cursor = response.NextCursor;
        } while (cursor is not null);
        Assert.Equal(105, ids.Count); Assert.Equal(105, ids.Distinct().Count());
        Assert.Equal(100, (await artist.GetFromJsonAsync<Page<ProposalView>>(path + "?limit=100"))!.Items.Count);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(path + "?limit=101")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(path + "?limit=0")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await third.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await third.GetAsync(path + "/" + firstId)).StatusCode);
        var encoded = Uri.EscapeDataString(page.NextCursor!);
        Assert.Equal(HttpStatusCode.BadRequest, (await artist.GetAsync(path + "?cursor=" + encoded)).StatusCode);
        var other = await RequestAsync(client);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/v2/engagements/{other.Id}/proposals?cursor={encoded}")).StatusCode);
        var detail = (await client.GetFromJsonAsync<JsonObject>($"/api/v2/engagements/{e.Id}"))!;
        Assert.False(detail.ContainsKey("proposals")); Assert.Equal(lastId, detail["currentProposal"]!["id"]!.GetValue<Guid>());
        var list = (await client.GetFromJsonAsync<JsonObject>("/api/v2/engagements?limit=100"))!;
        Assert.All(list["items"]!.AsArray(), item => Assert.False(item!.AsObject().ContainsKey("proposals")));
        Assert.Equal("Superseded", (await client.GetFromJsonAsync<ProposalView>(path + "/" + firstId))!.State);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsync(path + $"/{firstId}/acceptance", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsync(path + $"/{lastId}/acceptance", null)).StatusCode);
    }

    [Fact]
    public async Task Retired_version_does_not_silently_change_its_response_shape()
    {
        using var client = Client();
        var response = await client.GetAsync("/api/v1/engagements");
        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        Assert.Equal("engagements.api_version_retired", (await response.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>());
        using var gateway = fixture.Gateway(); using var proxy = gateway.CreateClient();
        proxy.DefaultRequestHeaders.Authorization = client.DefaultRequestHeaders.Authorization;
        Assert.Equal(HttpStatusCode.Gone, (await proxy.GetAsync("/api/v1/engagements")).StatusCode);
    }

    [Fact]
    public async Task Openapi_and_health_describe_the_actual_contract()
    {
        using var http = Client();
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/health/ready")).StatusCode);
        var schema = (await http.GetFromJsonAsync<JsonObject>("/openapi/v1.json"))!;
        var operation = schema["paths"]!["/api/v2/engagements"]!["post"]!;
        Assert.Contains(operation["parameters"]!.AsArray(), p => p!["name"]!.GetValue<string>() == "Idempotency-Key" && p["required"]!.GetValue<bool>());
        foreach (var code in new[] { "200", "201", "400", "401", "403", "404", "409", "500", "503" }) Assert.NotNull(operation["responses"]![code]);
        Assert.NotNull(operation["security"]);
        var schemas = schema["components"]!["schemas"]!;
        var request = schemas["RequestInput"]!["properties"]!;
        Assert.Equal(4000, request["idea"]!["maxLength"]!.GetValue<int>());
        Assert.Equal(120, request["bodyRegion"]!["maxLength"]!.GetValue<int>());
        Assert.Equal(1, request["idea"]!["minLength"]!.GetValue<int>());
        Assert.Equal(@"\S", request["idea"]!["pattern"]!.GetValue<string>());
        Assert.Equal(0.01m, request["approximateSizeCm"]!["minimum"]!.GetValue<decimal>());
        Assert.Equal(300m, request["approximateSizeCm"]!["maximum"]!.GetValue<decimal>());
        Assert.Equal(0.01m, request["approximateSizeCm"]!["multipleOf"]!.GetValue<decimal>());
        Assert.Contains("00000000", request["artistId"]!["pattern"]!.GetValue<string>());
        var proposal = schemas["ProposalInput"]!["properties"]!;
        Assert.Equal(4000, proposal["description"]!["maxLength"]!.GetValue<int>());
        Assert.Equal(0.01m, proposal["amount"]!["minimum"]!.GetValue<decimal>());
        Assert.Equal(9999999.99m, proposal["amount"]!["maximum"]!.GetValue<decimal>());
        Assert.Equal(0.01m, proposal["amount"]!["multipleOf"]!.GetValue<decimal>());
        Assert.NotNull(schema["paths"]!["/api/v2/engagements/{id}/proposals"]!["get"]);
    }
}
