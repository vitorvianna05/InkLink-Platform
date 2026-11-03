using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using InkLink.Engagements.Application.Negotiations;
using InkLink.Finance.Application.Ledger;
using InkLink.Messaging.Application.Conversations;
using InkLink.Messaging.Infrastructure.Persistence;
using InkLink.Scheduling.Application.Reservations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Yarp.ReverseProxy.Forwarder;
namespace InkLink.Finance.IntegrationTests;

[Collection("finance")]
public sealed class FinanceApiTests(FinanceFixture f)
{
    private static DateTimeOffset At(DateOnly day, int hour, int minute = 0) => new(day.Year, day.Month, day.Day, hour, minute, 0, TimeSpan.FromHours(-3));
    private static Task<HttpResponseMessage> Post(HttpClient client, string path, object body, Guid? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", (key ?? Guid.NewGuid()).ToString()); return client.SendAsync(request);
    }
    private async Task<EngagementView> Engagement(string client = "alice", decimal amount = 450)
    {
        using var catalog = f.Base.Base.Catalog.CreateClient(); catalog.DefaultRequestHeaders.Authorization = new("Bearer", f.Base.Base.Token("bob"));
        var published = await catalog.PutAsJsonAsync("/api/v1/artists/me", new { name = "Bob Finance", studio = "Studio", city = "São Paulo", biography = "Portfolio", styles = new[] { "Blackwork" }, startingPrice = 300 });
        Assert.True(published.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created);
        var artist = (await published.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<Guid>();
        using var customer = f.Base.Base.Engagements.CreateClient(); customer.DefaultRequestHeaders.Authorization = new("Bearer", f.Base.Base.Token(client));
        using var bob = f.Base.Base.Engagements.CreateClient(); bob.DefaultRequestHeaders.Authorization = new("Bearer", f.Base.Base.Token("bob"));
        var response = await Post(customer, "/api/v2/engagements", new RequestInput(artist, "Projeto Finance", "Braço", 10));
        var e = (await response.Content.ReadFromJsonAsync<EngagementView>())!;
        var proposal = await Post(bob, $"/api/v2/engagements/{e.Id}/proposals", new ProposalInput("Sessão", amount));
        var p = (await proposal.Content.ReadFromJsonAsync<ProposalView>())!;
        Assert.Equal(HttpStatusCode.OK, (await customer.PutAsync($"/api/v2/engagements/{e.Id}/proposals/{p.Id}/acceptance", null)).StatusCode);
        return e;
    }
    private async Task Available(DateOnly day)
    {
        using var bob = f.ScheduleClient();
        Assert.Equal(HttpStatusCode.OK, (await bob.PutAsJsonAsync($"/api/v1/scheduling/availability/{day:yyyy-MM-dd}",
            new { windows = new[] { new { start = "08:00:00", end = "18:00:00" } } })).StatusCode);
    }
    private async Task<ReservationView> Reserve(Guid engagement, DateOnly day, int start)
    {
        using var bob = f.ScheduleClient();
        var response = await Post(bob, "/api/v1/scheduling/reservations", new ReserveInput(engagement, At(day, start), At(day, start + 1)));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); return (await response.Content.ReadFromJsonAsync<ReservationView>())!;
    }
    private async Task<EntryView> Record(HttpClient client, EntryInput input, Guid? key = null)
    {
        var response = await Post(client, "/api/v1/finance/entries", input, key);
        Assert.True(response.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<EntryView>())!;
    }
    private static string Period(DateOnly day) => $"from={day:yyyy-MM-dd}&to={day.AddDays(1):yyyy-MM-dd}";
    private async Task<Dashboard> Dashboard(HttpClient client, DateOnly day) => (await client.GetFromJsonAsync<Dashboard>("/api/v1/finance/dashboard?" + Period(day)))!;

    [Fact]
    public async Task Dashboard_uses_real_sources_and_separates_booking_completion_and_receipts()
    {
        var day = f.NextDay(); await Available(day); var first = await Engagement(); var other = await Engagement("carol", 600);
        var one = await Reserve(first.Id, day, 10); var two = await Reserve(first.Id, day, 12); var three = await Reserve(other.Id, day, 14);
        using var bob = f.Client();
        var scheduled = await Dashboard(bob, day);
        Assert.Equal(0, scheduled.Financial.Received); Assert.Equal(3, scheduled.ScheduledReservations); Assert.Equal(1500, scheduled.ScheduledValue);
        using var schedule = f.ScheduleClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await schedule.PutAsJsonAsync($"/api/v1/scheduling/reservations/{one.Id}/completion", new CompletionInput(one.End))).StatusCode);
        f.Clock.Set(At(day, 18));
        Assert.Equal(HttpStatusCode.OK, (await schedule.PutAsJsonAsync($"/api/v1/scheduling/reservations/{one.Id}/completion", new CompletionInput(one.End, 500))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await schedule.PutAsJsonAsync($"/api/v1/scheduling/reservations/{two.Id}/completion", new CompletionInput(two.End, 700))).StatusCode);
        var completed = await Dashboard(bob, day);
        Assert.Equal(0, completed.Financial.Received); Assert.Equal(2, completed.CompletedSessions); Assert.Equal(1, completed.ClientsServed);
        Assert.Equal(600, completed.AverageSessionValue); Assert.Equal(1, completed.ScheduledReservations); Assert.Equal(600, completed.ScheduledValue);
        Assert.Empty((await bob.GetFromJsonAsync<InkLink.Finance.Application.Ledger.Page<EntryView>>("/api/v1/finance/entries?" + Period(day)))!.Items);
        await Record(bob, new("Receipt", "Parcela um", 2500, day, first.Id));
        await Record(bob, new("Receipt", "Parcela dois", 1500, day, first.Id));
        await Record(bob, new("Cost", "Materiais", 940, day));
        var result = await Dashboard(bob, day);
        Assert.Equal(4000, result.Financial.Received); Assert.Equal(940, result.Financial.Costs);
        Assert.Equal(3060, result.Financial.EstimatedProfit); Assert.Equal(76.5m, result.Financial.EstimatedMargin);
        Assert.Equal("America/Sao_Paulo", result.TimeZoneId);
        Assert.Equal(HttpStatusCode.OK, (await schedule.PutAsJsonAsync($"/api/v1/scheduling/reservations/{three.Id}/completion", new CompletionInput(three.End))).StatusCode);
        var after = await Dashboard(bob, day);
        Assert.Equal(3, after.CompletedSessions); Assert.Equal(2, after.ClientsServed); Assert.Equal(600, after.AverageSessionValue);
        Assert.Equal(0, after.ScheduledReservations); Assert.Equal(4000, after.Financial.Received);
        // Rebuilding the response from authoritative sources does not write or duplicate financial entries.
        Assert.Equal(after.Financial, (await Dashboard(bob, day)).Financial);
        using var restarted = new FinanceHost(f); using var fresh = restarted.CreateClient();
        fresh.DefaultRequestHeaders.Authorization = new("Bearer", f.Base.Base.Token("bob"));
        Assert.Equal(after.Financial, (await Dashboard(fresh, day)).Financial);
    }
    [Fact]
    public async Task Repetition_reversal_and_concurrency_preserve_an_immutable_ledger()
    {
        var day = f.NextDay(); f.Clock.Set(At(day, 12)); using var bob = f.Client(); var key = Guid.NewGuid();
        var input = new EntryInput("Cost", "Insumos", 100, day);
        var responses = await Task.WhenAll(Post(bob, "/api/v1/finance/entries", input, key), Post(bob, "/api/v1/finance/entries", input, key));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Created); Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        var original = (await responses[0].Content.ReadFromJsonAsync<EntryView>())!;
        Assert.Equal(HttpStatusCode.Conflict, (await Post(bob, "/api/v1/finance/entries", input with { Amount = 101 }, key)).StatusCode);
        f.UnavailableHost = "scheduling.test";
        Assert.Equal(HttpStatusCode.OK, (await Post(bob, "/api/v1/finance/entries", input, key)).StatusCode);
        var reversalKey = Guid.NewGuid(); var path = $"/api/v1/finance/entries/{original.Id}/reversal";
        var reversed = await Task.WhenAll(Post(bob, path, new ReversalInput("Correção"), reversalKey), Post(bob, path, new ReversalInput("Correção"), reversalKey));
        Assert.Single(reversed, x => x.StatusCode == HttpStatusCode.Created); Assert.Single(reversed, x => x.StatusCode == HttpStatusCode.OK);
        var entry = (await reversed[0].Content.ReadFromJsonAsync<EntryView>())!;
        Assert.Equal(original.Id, entry.OriginalEntryId); Assert.Equal(-100, entry.SignedAmount); Assert.Equal(day, entry.EffectiveDate);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(bob, path, new ReversalInput("Outro estorno"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(bob, $"/api/v1/finance/entries/{entry.Id}/reversal", new ReversalInput("Inválido"))).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await bob.DeleteAsync($"/api/v1/finance/entries/{original.Id}")).StatusCode);
        f.UnavailableHost = null;
        Assert.Equal(0, (await Dashboard(bob, day)).Financial.Costs);
        Assert.Equal(original, await bob.GetFromJsonAsync<EntryView>($"/api/v1/finance/entries/{original.Id}"));
    }
    [Fact]
    public async Task Roles_ownership_pagination_and_dependency_failures_are_explicit()
    {
        var day = f.NextDay(); f.Clock.Set(At(day, 12)); using var bob = f.Client(); using var alice = f.Client("alice"); using var carol = f.Client("carol"); using var anonymous = f.Client(null);
        var own = await Record(bob, new("Cost", "Custo privado", 50, day));
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/api/v1/finance/entries/{own.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await carol.GetAsync("/api/v1/finance/entries?" + Period(day))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/finance/entries?" + Period(day))).StatusCode);
        Assert.Equal(0, (await Dashboard(alice, day)).Financial.Costs);
        await Record(bob, new("Cost", "Mais um", 75, day));
        var page = (await bob.GetFromJsonAsync<InkLink.Finance.Application.Ledger.Page<EntryView>>("/api/v1/finance/entries?" + Period(day) + "&limit=1"))!;
        Assert.True(page.HasMore); Assert.Equal(2, page.Total);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.GetAsync("/api/v1/finance/entries?" + Period(day) + "&cursor=" + Uri.EscapeDataString(page.NextCursor!))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await bob.GetAsync("/api/v1/finance/entries?" + Period(day.AddDays(1)) + "&cursor=" + Uri.EscapeDataString(page.NextCursor!))).StatusCode);
        var next = (await bob.GetFromJsonAsync<InkLink.Finance.Application.Ledger.Page<EntryView>>("/api/v1/finance/entries?" + Period(day) + "&cursor=" + Uri.EscapeDataString(page.NextCursor!)))!;
        Assert.Single(next.Items); Assert.NotEqual(page.Items.Single().Id, next.Items.Single().Id);
        f.UnavailableHost = "messaging.test";
        var error = await bob.GetAsync("/api/v1/finance/dashboard?" + Period(day));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, error.StatusCode);
        Assert.Equal("application/problem+json", error.Content.Headers.ContentType!.MediaType);
        Assert.NotNull((await error.Content.ReadFromJsonAsync<JsonObject>())!["traceId"]);
        f.UnavailableHost = "invalid:messaging.test";
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await bob.GetAsync("/api/v1/finance/dashboard?" + Period(day))).StatusCode);
        f.UnavailableHost = null;
    }
    [Theory]
    [InlineData("scheduling.test", "/api/v1/scheduling/artist-summary")]
    [InlineData("messaging.test", "/api/v1/conversations/artist-summary")]
    public async Task Dashboard_rejects_missing_or_null_source_fields_without_converting_them_to_zero(string host, string path)
    {
        var day = f.NextDay(); using var bob = f.Client();
        Assert.NotNull(await Dashboard(bob, day));
        string[] fields = host == "scheduling.test"
            ? ["artistId", "timeZoneId", "completedSessions", "clientsServed", "completedValue", "scheduledReservations", "scheduledValue", "observedAt"]
            : ["artistId", "activeConversations", "observedAt"];
        try
        {
            foreach (var field in fields)
                foreach (var setNull in new[] { false, true })
                {
                    f.SourceFieldFault = (host, path, field, setNull);
                    var response = await bob.GetAsync("/api/v1/finance/dashboard?" + Period(day));
                    Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
                    var problem = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
                    Assert.Equal("finance.source_unavailable", problem["code"]!.GetValue<string>());
                    Assert.NotNull(problem["traceId"]);
                }
        }
        finally { f.SourceFieldFault = null; }
        Assert.NotNull(await Dashboard(bob, day));
    }
    [Fact]
    public async Task Record_rejects_incomplete_calendar_context_without_writing_a_ledger_entry()
    {
        var day = f.NextDay(); f.Clock.Set(At(day, 12)); using var bob = f.Client();
        try
        {
            foreach (var field in new[] { "artistId", "timeZoneId", "observedAt" })
                foreach (var setNull in new[] { false, true })
                {
                    f.SourceFieldFault = ("scheduling.test", "/api/v1/scheduling/artist-context", field, setNull);
                    Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Post(bob, "/api/v1/finance/entries", new EntryInput("Cost", "Contexto incompleto", 10, day))).StatusCode);
                }
        }
        finally { f.SourceFieldFault = null; }
        Assert.Empty((await bob.GetFromJsonAsync<InkLink.Finance.Application.Ledger.Page<EntryView>>("/api/v1/finance/entries?" + Period(day)))!.Items);
    }
    [Fact]
    public async Task Dashboard_reads_backdated_cash_on_a_day_with_a_midnight_dst_gap()
    {
        f.NextDay(); using var bob = f.Client(); var day = new DateOnly(2018, 11, 4);
        await Record(bob, new("Cost", "Custo histórico", 125, day));
        var result = await Dashboard(bob, day);
        Assert.Equal(125, result.Financial.Costs);
        Assert.Equal(0, result.CompletedSessions);
        var following = await Dashboard(bob, day.AddDays(1));
        Assert.Equal(0, following.Financial.Costs);
    }
    [Fact]
    public async Task Completion_enforces_ownership_idempotency_and_local_period_boundaries()
    {
        var day = f.NextDay(); await Available(day); var e = await Engagement(); var reservation = await Reserve(e.Id, day, 10);
        f.Clock.Set(At(day, 23, 50)); var instant = At(day, 23, 30);
        using var bob = f.ScheduleClient(); using var alice = f.ScheduleClient("alice"); using var carol = f.ScheduleClient("carol");
        var path = $"/api/v1/scheduling/reservations/{reservation.Id}/completion"; var input = new CompletionInput(instant, 800);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.PutAsJsonAsync(path, input)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await carol.PutAsJsonAsync(path, input)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await bob.PutAsJsonAsync(path, input)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await bob.PutAsJsonAsync(path, input)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await bob.PutAsJsonAsync(path, input with { Amount = 801 })).StatusCode);
        using var finance = f.Client();
        var result = await Dashboard(finance, day);
        Assert.Equal(1, result.CompletedSessions); Assert.Equal(800, result.AverageSessionValue);
        Assert.Equal(0, (await Dashboard(finance, day.AddDays(1))).CompletedSessions);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(finance, "/api/v1/finance/entries", new EntryInput("Cost", "Amanhã local", 10, day.AddDays(1)))).StatusCode);
        var receipt = await Record(finance, new("Receipt", "Recebido ontem", 450, day.AddDays(-1), e.Id));
        Assert.Equal(0, (await Dashboard(finance, day)).Financial.Received);
        Assert.Equal(450, (await Dashboard(finance, receipt.EffectiveDate)).Financial.Received);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0.001)]
    [InlineData(10000000)]
    public async Task Invalid_money_does_not_create_entries(decimal amount)
    {
        var day = f.NextDay(); f.Clock.Set(At(day, 12)); using var bob = f.Client();
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(bob, "/api/v1/finance/entries", new EntryInput("Cost", "Inválido", amount, day))).StatusCode);
        Assert.Empty((await bob.GetFromJsonAsync<InkLink.Finance.Application.Ledger.Page<EntryView>>("/api/v1/finance/entries?" + Period(day)))!.Items);
    }
    [Fact]
    public async Task Receipt_requires_artist_ownership_and_active_chat_count_is_current()
    {
        var day = f.NextDay(); var e = await Engagement(); f.Clock.Set(At(day, 12));
        using var bob = f.Client(); using var alice = f.Client("alice");
        Assert.Equal(HttpStatusCode.NotFound, (await Post(alice, "/api/v1/finance/entries", new EntryInput("Receipt", "Não sou artista deste serviço", 100, day, e.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(bob, "/api/v1/finance/entries", new EntryInput("Receipt", "Sem contexto", 100, day))).StatusCode);
        var before = (await Dashboard(bob, day)).CurrentActiveConversations;
        await using (var db = f.ChatContext())
            await new OpenConversation(new EfConversationStore(db)).ExecuteAsync(new(Guid.NewGuid(), "engagement.negotiation-opened.v1", 1, DateTimeOffset.UtcNow, "p08", null,
                new(e.Id, e.ClientId, e.ArtistAccountId, 1, true)), default);
        Assert.Equal(before + 1, (await Dashboard(bob, day)).CurrentActiveConversations);
        Assert.Equal(before + 1, (await Dashboard(bob, day.AddDays(-10))).CurrentActiveConversations);
        using var tech = f.Messaging.CreateClient(); tech.DefaultRequestHeaders.Authorization = new("Bearer", f.Base.Base.Token("svc", "service", "inklink-engagements", "messaging.conversation.lifecycle"));
        Assert.Equal(HttpStatusCode.NoContent, (await tech.PutAsJsonAsync($"/api/v1/internal/engagements/{e.Id}/lifecycle", new LifecycleInput(e.ClientId, e.ArtistAccountId, true))).StatusCode);
        Assert.Equal(before, (await Dashboard(bob, day)).CurrentActiveConversations);
    }
    [Fact]
    public async Task Gateway_routes_finance_and_documents_real_public_operations()
    {
        var day = f.NextDay(); f.Clock.Set(At(day, 12));
        using var gateway = f.Base.Base.Gateway().WithWebHostBuilder(b =>
        {
            b.UseSetting("Gateway:FinanceAddress", "http://finance.test/");
            b.ConfigureTestServices(s =>
            {
                s.AddSingleton<IForwarderHttpClientFactory>(new GatewayClients(f));
                s.AddHttpClient<InkLink.Gateway.Documentation.GatewayDocuments>().ConfigurePrimaryHttpMessageHandler(() => new GatewayRouter(f));
            });
        });
        using var client = gateway.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/finance/dashboard?" + Period(day))).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", f.Base.Base.Token("bob"));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/finance/dashboard?" + Period(day))).StatusCode);
        var document = (await client.GetFromJsonAsync<JsonObject>("/swagger/documents/finance.json"))!;
        Assert.NotNull(document["paths"]!["/api/v1/finance/entries"]);
        Assert.NotNull(document["paths"]!["/api/v1/finance/dashboard"]);
        Assert.Equal("/", document["servers"]![0]!["url"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/internal/finance")).StatusCode);
    }
    [Fact]
    public async Task Runtime_database_identity_cannot_access_other_context_or_change_schema()
    {
        var connection = new NpgsqlConnectionStringBuilder(f.App) { Database = new NpgsqlConnectionStringBuilder(f.Base.Base.OwnerConnection).Database };
        await using var other = new NpgsqlConnection(connection.ConnectionString);
        var denied = await Assert.ThrowsAsync<PostgresException>(() => other.OpenAsync());
        Assert.Equal("42501", denied.SqlState);
        await using var own = new NpgsqlConnection(f.App); await own.OpenAsync();
        await using var command = new NpgsqlCommand("CREATE TABLE finance.forbidden(id integer)", own);
        Assert.Equal("42501", (await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync())).SqlState);
    }
    private sealed class GatewayClients(FinanceFixture f) : IForwarderHttpClientFactory
    {
        public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) => new(new GatewayRouter(f));
    }
    private sealed class GatewayRouter(FinanceFixture f) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            using var invoker = new HttpMessageInvoker(request.RequestUri!.Host == "finance.test" ? f.Host.Server.CreateHandler() : f.Base.Base.Identity.Server.CreateHandler());
            return await invoker.SendAsync(request, ct);
        }
    }
}
