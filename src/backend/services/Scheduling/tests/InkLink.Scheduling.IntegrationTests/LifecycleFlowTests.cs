using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using InkLink.Engagements.Application.Lifecycle;
using InkLink.Engagements.Application.Negotiations;
using InkLink.Engagements.Application.Scheduling;
using InkLink.Engagements.Domain;
using InkLink.Engagements.Infrastructure.Integration;
using InkLink.Engagements.Infrastructure.Persistence;
using InkLink.Messaging.Application.Conversations;
using InkLink.Messaging.Infrastructure.Integration;
using InkLink.Messaging.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
namespace InkLink.Scheduling.IntegrationTests;

public sealed class LifecycleFlowTests
{
    [Theory]
    [InlineData("alice")]
    [InlineData("bob")]
    public async Task Closure_recovery_retains_history_and_return_requires_confirmed_reservation(string closer)
    {
        await using var harness = new Harness(); await harness.StartAsync();
        var f = harness.Fixture;
        var first = await Scenario.EngagementAsync(f);
        await harness.OpenAsync(first.Id);
        var conversation = await harness.ConversationAsync(first.Id);
        using var aliceChat = harness.Chat("alice");
        Assert.Equal(HttpStatusCode.Created, (await Scenario.PostAsync(aliceChat, $"/api/v1/conversations/{conversation}/messages", new { body = "Antes de encerrar" }, Guid.NewGuid())).StatusCode);
        using var actor = harness.Care(closer); using var stranger = harness.Care("carol");
        var path = $"/api/v2/engagements/{first.Id}";
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsJsonAsync(path + "/closure", new { confirmed = true })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await actor.PostAsJsonAsync(path + "/closure", new { confirmed = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await actor.PostAsJsonAsync(path + "/closure", new { confirmed = true })).StatusCode);
        var op = (await actor.GetFromJsonAsync<LifecycleStatus>(path + "/lifecycle"))!.Operations.Single();
        // Simulate acknowledgement loss after Messaging has committed its permanent barrier.
        await harness.RunAsync(op.Id, failAfterBlock: true);
        Assert.Equal("Closing", (await actor.GetFromJsonAsync<EngagementView>(path))!.State);
        Assert.Equal(HttpStatusCode.Conflict, (await Scenario.PostAsync(aliceChat, $"/api/v1/conversations/{conversation}/messages", new { body = "Bloqueado" }, Guid.NewGuid())).StatusCode);
        await harness.ReadyAsync(op.Id);
        await harness.RunAsync(op.Id); // new database context emulates restart
        Assert.Equal("Closed", (await actor.GetFromJsonAsync<EngagementView>(path))!.State);
        Assert.Equal(HttpStatusCode.OK, (await actor.PostAsJsonAsync(path + "/closure", new { confirmed = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(path + "/lifecycle")).StatusCode);
        var history = await aliceChat.GetFromJsonAsync<InkLink.Messaging.Application.Conversations.Page<MessageView>>($"/api/v1/conversations/{conversation}/messages");
        Assert.Equal("Antes de encerrar", Assert.Single(history!.Items).Body);
        await harness.OpenAsync(first.Id, duplicate: true); // late initial event cannot reopen it
        Assert.Equal(HttpStatusCode.Conflict, (await Scenario.PostAsync(aliceChat, $"/api/v1/conversations/{conversation}/messages", new { body = "Tardio" }, Guid.NewGuid())).StatusCode);

        var next = await Scenario.EngagementAsync(f);
        await harness.OpenAsync(next.Id);
        var newConversation = await harness.ConversationAsync(next.Id);
        Assert.NotEqual(conversation, newConversation);
        Assert.Equal(HttpStatusCode.Conflict, (await Scenario.PostAsync(aliceChat, $"/api/v1/conversations/{newConversation}/messages", new { body = "Sem reserva" }, Guid.NewGuid())).StatusCode);
        using var bob = Scenario.Client(f, "bob"); var day = Scenario.Day(); await Scenario.AvailableAsync(bob, day);
        var response = await Scenario.ReserveAsync(bob, next.Id, day);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var reservation = (await response.Content.ReadFromJsonAsync<InkLink.Scheduling.Application.Reservations.ReservationView>())!;
        await harness.DeliverReservationAsync(reservation.Id);
        await harness.DeliverReservationAsync(reservation.Id);
        using var alice = harness.Care("alice");
        var activation = (await alice.GetFromJsonAsync<LifecycleStatus>($"/api/v2/engagements/{next.Id}/lifecycle"))!.Operations.Single();
        await harness.RunAsync(activation.Id);
        Assert.Equal(HttpStatusCode.Created, (await Scenario.PostAsync(aliceChat, $"/api/v1/conversations/{newConversation}/messages", new { body = "Novo atendimento" }, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Scenario.PostAsync(aliceChat, $"/api/v1/conversations/{conversation}/messages", new { body = "Antigo" }, Guid.NewGuid())).StatusCode);
        // A future confirmed session prevents closure and keeps the new chat available.
        await alice.PostAsJsonAsync($"/api/v2/engagements/{next.Id}/closure", new { confirmed = true });
        var close = (await alice.GetFromJsonAsync<LifecycleStatus>($"/api/v2/engagements/{next.Id}/lifecycle"))!.Operations.Single(x => x.Kind == "Close");
        await harness.RunAsync(close.Id);
        var state = (await alice.GetFromJsonAsync<LifecycleStatus>($"/api/v2/engagements/{next.Id}/lifecycle"))!;
        Assert.Equal("Agreed", state.State);
        Assert.Equal("engagements.future_reservations", state.Operations.Single(x => x.Kind == "Close").Error);
        using var client = Scenario.Client(f, "alice");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/scheduling/reservations/{reservation.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Scenario.PostAsync(aliceChat, $"/api/v1/conversations/{newConversation}/messages", new { body = "Reserva preservada" }, Guid.NewGuid())).StatusCode);
    }

    [Fact]
    public async Task Close_before_opening_and_old_activation_never_override_permanent_barrier()
    {
        await using var h = new Harness(); await h.StartAsync();
        var e = await Scenario.EngagementAsync(h.Fixture);
        using var actor = h.Care("alice");
        await actor.PostAsJsonAsync($"/api/v2/engagements/{e.Id}/closure", new { confirmed = true });
        var op = (await actor.GetFromJsonAsync<LifecycleStatus>($"/api/v2/engagements/{e.Id}/lifecycle"))!.Operations.Single();
        await h.RunAsync(op.Id);
        await h.OpenAsync(e.Id); // opening was delayed until after closure
        var id = await h.ConversationAsync(e.Id);
        using var chat = h.Chat("bob");
        Assert.Equal(HttpStatusCode.Conflict, (await Scenario.PostAsync(chat, $"/api/v1/conversations/{id}/messages", new { body = "Evento atrasado" }, Guid.NewGuid())).StatusCode);
        using var admin = h.TechnicalChat();
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"/api/v1/internal/engagements/{e.Id}/lifecycle",
            new LifecycleInput(h.Fixture.Base.Accounts["alice"], h.Fixture.Base.Accounts["bob"], false))).StatusCode);
        foreach (var user in new[] { "alice", "carol" })
        {
            using var invalid = h.Chat(user);
            Assert.Equal(HttpStatusCode.Forbidden, (await invalid.PutAsJsonAsync($"/api/v1/internal/engagements/{e.Id}/lifecycle",
                new LifecycleInput(h.Fixture.Base.Accounts["alice"], h.Fixture.Base.Accounts["bob"], false))).StatusCode);
        }
    }

    [Fact]
    public async Task Freeze_and_confirmation_race_cannot_both_succeed()
    {
        await using var h = new Harness(); await h.StartAsync();
        var f = h.Fixture; var e = await Scenario.EngagementAsync(f);
        using var bob = Scenario.Client(f, "bob"); var day = Scenario.Day(); await Scenario.AvailableAsync(bob, day);
        using var tech = f.Host.CreateClient();
        tech.DefaultRequestHeaders.Authorization = new("Bearer", f.Base.Token("svc", "service", "inklink-engagements", "scheduling.engagement.freeze"));
        var responses = await Task.WhenAll(Scenario.ReserveAsync(bob, e.Id, day),
            tech.PutAsJsonAsync($"/api/v1/internal/engagements/{e.Id}/freeze", new { e.ClientId, e.ArtistAccountId }));
        Assert.True(responses[0].StatusCode == HttpStatusCode.Created || responses[1].StatusCode == HttpStatusCode.NoContent);
        Assert.False(responses[0].StatusCode == HttpStatusCode.Created && responses[1].StatusCode == HttpStatusCode.NoContent);
        Assert.Contains(HttpStatusCode.Conflict, responses.Select(x => x.StatusCode));
    }

    [Fact]
    public async Task Exhausted_operations_are_durable_and_participant_can_resume_after_restart()
    {
        await using var h = new Harness(); await h.StartAsync();
        var e = await Scenario.EngagementAsync(h.Fixture);
        using var actor = h.Care("bob");
        var path = $"/api/v2/engagements/{e.Id}";
        await actor.PostAsJsonAsync(path + "/closure", new { confirmed = true });
        var op = (await actor.GetFromJsonAsync<LifecycleStatus>(path + "/lifecycle"))!.Operations.Single();
        for (var i = 0; i < 10; i++) { await h.ReadyAsync(op.Id); await h.RunAsync(op.Id, true); }
        var state = (await actor.GetFromJsonAsync<LifecycleStatus>(path + "/lifecycle"))!;
        Assert.Equal("Closing", state.State);
        Assert.Equal("Quarantined", state.Operations.Single().Status);
        Assert.Equal(10, state.Operations.Single().Attempts);
        using var stranger = h.Care("carol");
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsync(path + "/lifecycle/retry", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await actor.PostAsync(path + "/lifecycle/retry", null)).StatusCode);
        await h.RunAsync(op.Id);
        Assert.Equal("Closed", (await actor.GetFromJsonAsync<EngagementView>(path))!.State);
    }

    [Fact]
    public async Task Real_Keycloak_scopes_authorize_only_the_matching_technical_operation()
    {
        var provider = new InkLink.Identity.IntegrationTests.SecurityFixture();
        await using var h = new Harness();
        try
        {
            await provider.InitializeAsync();
            h.Fixture.Base.UseRealIdentity(provider);
            await h.StartAsync();
            using var tokens = new HttpClient(provider.CreateProviderHandler());
            async Task<string> Token(string scope)
            {
                using var response = await tokens.PostAsync(provider.Authority + "/protocol/openid-connect/token",
                    new FormUrlEncodedContent(new Dictionary<string, string>
                    { ["grant_type"] = "client_credentials", ["client_id"] = "inklink-engagements", ["client_secret"] = provider.EngagementsSecret, ["scope"] = scope }));
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                return (await response.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>())!["access_token"]!.GetValue<string>();
            }
            var e = await Scenario.EngagementAsync(h.Fixture);
            using var scheduling = h.Fixture.Host.CreateClient();
            scheduling.DefaultRequestHeaders.Authorization = new("Bearer", await Token("scheduling.engagement.freeze"));
            Assert.Equal(HttpStatusCode.NoContent, (await scheduling.PutAsJsonAsync($"/api/v1/internal/engagements/{e.Id}/freeze",
                new { e.ClientId, e.ArtistAccountId })).StatusCode);
            using var chat = h.RawChat();
            chat.DefaultRequestHeaders.Authorization = new("Bearer", await Token("messaging.conversation.lifecycle"));
            Assert.Equal(HttpStatusCode.NoContent, (await chat.PutAsJsonAsync($"/api/v1/internal/engagements/{e.Id}/lifecycle",
                new LifecycleInput(e.ClientId, e.ArtistAccountId, true))).StatusCode);
            scheduling.DefaultRequestHeaders.Authorization = chat.DefaultRequestHeaders.Authorization;
            Assert.Equal(HttpStatusCode.Forbidden, (await scheduling.PutAsJsonAsync($"/api/v1/internal/engagements/{e.Id}/freeze",
                new { e.ClientId, e.ArtistAccountId })).StatusCode);
        }
        finally { await h.DisposeAsync(); await provider.DisposeAsync(); }
    }

    [Fact]
    public async Task Registered_worker_recovers_a_persisted_request_with_runtime_credentials()
    {
        await using var h = new Harness(); await h.StartAsync();
        var e = await Scenario.EngagementAsync(h.Fixture);
        var traces = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => { if (activity.Source.Name == "InkLink.Engagements.Lifecycle") traces.Add(activity); }
        };
        ActivitySource.AddActivityListener(listener);
        LifecycleOperation operation;
        using (var actor = h.Care("alice"))
        {
            actor.DefaultRequestHeaders.Add("traceparent", "00-1234567890abcdef1234567890abcdef-1234567890abcdef-01");
            using var response = await actor.PostAsJsonAsync($"/api/v2/engagements/{e.Id}/closure", new { confirmed = true });
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            await using var db = h.Fixture.Base.CreateOwnerContext();
            operation = await db.LifecycleOperations.AsNoTracking().SingleAsync(x => x.EngagementId == e.Id);
            Assert.True(ActivityContext.TryParse(operation.TraceParent, null, out var parent));
            Assert.Equal(response.Headers.GetValues("X-Trace-Id").Single(), parent.TraceId.ToString());
            Assert.Equal(parent.TraceId.ToString(), operation.CorrelationId);
        }
        using var worker = h.WorkerHost();
        using var client = worker.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", h.Fixture.Base.Token("alice"));
        var until = DateTimeOffset.UtcNow.AddSeconds(30);
        LifecycleStatus? state;
        do
        {
            state = await client.GetFromJsonAsync<LifecycleStatus>($"/api/v2/engagements/{e.Id}/lifecycle");
            if (state!.State == "Closed") break;
            await Task.Delay(100);
        } while (DateTimeOffset.UtcNow < until);
        Assert.Equal("Closed", state!.State);
        Assert.Equal("Completed", state.Operations.Single().Status);
        Assert.True(ActivityContext.TryParse(operation.TraceParent, null, out var source));
        Assert.Contains(traces, activity => activity.TraceId == source.TraceId && activity.ParentSpanId == source.SpanId);
        await h.OpenAsync(e.Id);
        using var chat = h.Chat("alice"); var id = await h.ConversationAsync(e.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await Scenario.PostAsync(chat, $"/api/v1/conversations/{id}/messages", new { body = "Após worker" }, Guid.NewGuid())).StatusCode);
    }

    [Fact]
    public async Task Activation_waits_for_rejected_closure_without_exhausting_retry_budget()
    {
        await using var h = new Harness(); await h.StartAsync();
        var original = await Scenario.EngagementAsync(h.Fixture);
        using var actor = h.Care("alice");
        await actor.PostAsJsonAsync($"/api/v2/engagements/{original.Id}/closure", new { confirmed = true });
        var initial = (await actor.GetFromJsonAsync<LifecycleStatus>($"/api/v2/engagements/{original.Id}/lifecycle"))!.Operations.Single();
        await h.RunAsync(initial.Id);
        var next = await Scenario.EngagementAsync(h.Fixture);
        await h.OpenAsync(next.Id);
        using var bob = Scenario.Client(h.Fixture, "bob"); var day = Scenario.Day(); await Scenario.AvailableAsync(bob, day);
        var response = await Scenario.ReserveAsync(bob, next.Id, day);
        var reservation = (await response.Content.ReadFromJsonAsync<InkLink.Scheduling.Application.Reservations.ReservationView>())!;
        await h.DeliverReservationAsync(reservation.Id);
        var path = $"/api/v2/engagements/{next.Id}";
        var activation = (await actor.GetFromJsonAsync<LifecycleStatus>(path + "/lifecycle"))!.Operations.Single();
        await actor.PostAsJsonAsync(path + "/closure", new { confirmed = true });
        for (var attempt = 0; attempt < 12; attempt++) { await h.ReadyAsync(activation.Id); await h.RunAsync(activation.Id); }
        var waiting = (await actor.GetFromJsonAsync<LifecycleStatus>(path + "/lifecycle"))!;
        Assert.Equal("Closing", waiting.State);
        Assert.Equal("Pending", waiting.Operations.Single(x => x.Kind == "Activate").Status);
        Assert.Equal(0, waiting.Operations.Single(x => x.Kind == "Activate").Attempts);
        await h.RunAsync(waiting.Operations.Single(x => x.Kind == "Close").Id);
        Assert.Equal("Agreed", (await actor.GetFromJsonAsync<LifecycleStatus>(path + "/lifecycle"))!.State);
        await h.ReadyAsync(activation.Id); await h.RunAsync(activation.Id);
        Assert.Equal("Completed", (await actor.GetFromJsonAsync<LifecycleStatus>(path + "/lifecycle"))!.Operations.Single(x => x.Kind == "Activate").Status);
        using var chat = h.Chat("alice"); var id = await h.ConversationAsync(next.Id);
        Assert.Equal(HttpStatusCode.Created, (await Scenario.PostAsync(chat, $"/api/v1/conversations/{id}/messages", new { body = "Ativação recuperada" }, Guid.NewGuid())).StatusCode);
    }

    [Fact]
    public async Task Reservation_context_is_preserved_without_an_ambient_activity()
    {
        await using var h = new Harness(); await h.StartAsync();
        var original = await Scenario.EngagementAsync(h.Fixture);
        using var actor = h.Care("alice");
        await actor.PostAsJsonAsync($"/api/v2/engagements/{original.Id}/closure", new { confirmed = true });
        var initial = (await actor.GetFromJsonAsync<LifecycleStatus>($"/api/v2/engagements/{original.Id}/lifecycle"))!.Operations.Single();
        await h.RunAsync(initial.Id);
        var next = await Scenario.EngagementAsync(h.Fixture);
        using var bob = Scenario.Client(h.Fixture, "bob"); var day = Scenario.Day(); await Scenario.AvailableAsync(bob, day);
        var response = await Scenario.ReserveAsync(bob, next.Id, day);
        var reservation = (await response.Content.ReadFromJsonAsync<InkLink.Scheduling.Application.Reservations.ReservationView>())!;
        const string parent = "00-abcdefabcdefabcdefabcdefabcdefab-abcdefabcdefabcd-01";
        var previous = Activity.Current;
        try { Activity.Current = null; await h.DeliverReservationAsync(reservation.Id, parent, "reservation-correlation"); }
        finally { Activity.Current = previous; }
        await using var db = h.Fixture.Base.CreateOwnerContext();
        var operation = await db.LifecycleOperations.AsNoTracking().SingleAsync(x => x.EngagementId == next.Id && x.Kind == "Activate");
        Assert.Equal(parent, operation.TraceParent);
        Assert.Equal("reservation-correlation", operation.CorrelationId);
        // Duplicate facts retain the original operation and its correlation.
        await h.DeliverReservationAsync(reservation.Id, parent, "reservation-correlation");
        Assert.Equal(1, await db.LifecycleOperations.CountAsync(x => x.EngagementId == next.Id && x.Kind == "Activate"));
    }

    [Fact]
    public async Task Migration_recovers_legacy_rejected_activation_but_never_a_closed_service()
    {
        await using var h = new Harness(); await h.StartAsync();
        var connection = await h.Fixture.Base.DatabaseAsync("engagements");
        var options = new DbContextOptionsBuilder<EngagementsDbContext>();
        InkLink.Engagements.Infrastructure.DependencyInjection.Configure(options, connection.Owner);
        await using var db = new EngagementsDbContext(options.Options);
        await db.GetService<IMigrator>().MigrateAsync("20261103172545_LifecycleCoordination");
        var ids = new List<Guid>();
        foreach (var closed in new[] { false, true })
        {
            var e = Engagement.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, "Contexto", "Braço", 10, "{}", Guid.NewGuid(), "test", DateTimeOffset.UtcNow);
            e.RequireReservation();
            var proposal = Proposal.Create(e.Id, Guid.NewGuid(), "test", "Proposta", 100, DateTimeOffset.UtcNow);
            e.Present(proposal, null); e.Accept(proposal, DateTimeOffset.UtcNow); e.ReservationConfirmed();
            if (closed) { e.BeginClosing(); e.CompleteClosing(); }
            db.Engagements.Add(e); db.Proposals.Add(proposal); await db.SaveChangesAsync();
            var id = Guid.NewGuid(); ids.Add(id);
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO engagements.lifecycle_operations (\"Id\",\"EngagementId\",\"Kind\",\"Status\",\"PreviousState\",\"Attempts\",\"Error\",\"UpdatedAt\") VALUES ({id},{e.Id},'Activate','Rejected','Agreed',0,'engagements.closing',{DateTimeOffset.UtcNow})");
        }
        await db.Database.MigrateAsync(); db.ChangeTracker.Clear();
        var recovered = await db.LifecycleOperations.SingleAsync(x => x.Id == ids[0]);
        var permanent = await db.LifecycleOperations.SingleAsync(x => x.Id == ids[1]);
        Assert.Equal("Pending", recovered.Status); Assert.Null(recovered.TraceParent); Assert.Null(recovered.CorrelationId);
        Assert.Equal("Rejected", permanent.Status);
        await db.Database.MigrateAsync();
        Assert.Equal(2, await db.LifecycleOperations.CountAsync());
    }

    private sealed class Harness : IAsyncDisposable
    {
        public SchedulingFixture Fixture { get; } = new();
        private string owner = "", app = "";
        private ChatHost host = null!;
        public async Task StartAsync()
        {
            await Fixture.InitializeAsync();
            var db = await Fixture.Base.DatabaseAsync("messaging"); owner = db.Owner; app = db.App;
            await using var context = Messaging(); await context.Database.MigrateAsync();
            host = new ChatHost(Fixture, app);
        }
        public MessagingDbContext Messaging()
        {
            var options = new DbContextOptionsBuilder<MessagingDbContext>();
            InkLink.Messaging.Infrastructure.DependencyInjection.Configure(options, owner); return new(options.Options);
        }
        public WebApplicationFactory<EngagementsProgram> WorkerHost() => Fixture.Base.Engagements.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Engagements:SchedulingAddress", "http://scheduling.test/");
            builder.UseSetting("Engagements:MessagingAddress", "http://messaging.test/");
            builder.UseSetting("Lifecycle:EnableWorkers", "true");
            builder.ConfigureTestServices(services =>
            {
                services.AddHttpClient<ILifecycleDirectory, LifecycleDirectory>().ConfigurePrimaryHttpMessageHandler(() => new BackendRouter(Fixture, host));
                services.AddHttpClient<ServiceTokenClient>().ConfigurePrimaryHttpMessageHandler(() => new Tokens(Fixture));
            });
        });
        public HttpClient RawChat() => host.CreateClient();
        public HttpClient Care(string user)
        {
            var client = Fixture.Base.Engagements.CreateClient(); client.DefaultRequestHeaders.Authorization = new("Bearer", Fixture.Base.Token(user)); return client;
        }
        public HttpClient Chat(string user)
        {
            var client = host.CreateClient(); client.DefaultRequestHeaders.Authorization = new("Bearer", Fixture.Base.Token(user)); return client;
        }
        public HttpClient TechnicalChat()
        {
            var client = host.CreateClient(); client.DefaultRequestHeaders.Authorization = new("Bearer", Fixture.Base.Token("svc", "service", "inklink-engagements", "messaging.conversation.lifecycle")); return client;
        }
        public async Task<Guid> ConversationAsync(Guid e)
        {
            await using var db = Messaging(); return (await db.Conversations.SingleAsync(x => x.EngagementId == e)).Id;
        }
        public async Task OpenAsync(Guid id, bool duplicate = false)
        {
            await using var db = Fixture.Base.CreateOwnerContext();
            var payload = (await db.Outbox.SingleAsync(x => x.EngagementId == id)).Payload;
            var envelope = JsonSerializer.Deserialize<EventEnvelope>(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            if (duplicate) envelope = envelope with { MessageId = Guid.NewGuid() };
            await using var chat = Messaging(); await new OpenConversation(new EfConversationStore(chat)).ExecuteAsync(envelope, default);
        }
        public async Task DeliverReservationAsync(Guid reservation, string? traceParent = null, string? correlationId = null)
        {
            await using var scheduling = Fixture.CreateOwnerContext();
            var payload = (await scheduling.Outbox.SingleAsync(x => x.ReservationId == reservation)).Payload;
            var envelope = JsonSerializer.Deserialize<ReservationEnvelope>(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            if (traceParent is not null) envelope = envelope with { TraceParent = traceParent, CorrelationId = correlationId! };
            await using var care = Fixture.Base.CreateOwnerContext();
            await new RecordReservation(new EfReservationReceiptStore(care)).ExecuteAsync(envelope, default);
        }
        public async Task ReadyAsync(Guid op)
        {
            await using var db = Fixture.Base.CreateOwnerContext();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE engagements.lifecycle_operations SET \"NextAttemptAt\"=NULL WHERE \"Id\"={op}");
        }
        public async Task RunAsync(Guid op, bool failAfterBlock = false)
        {
            var directory = new LifecycleDirectory(new HttpClient(new BackendRouter(Fixture, host)),
                new ServiceTokenClient(new HttpClient(new Tokens(Fixture))),
                new(new Uri(Fixture.Base.Authority + "/protocol/openid-connect/token"), "inklink-engagements", Fixture.Base.ServiceSecret),
                new(new Uri("http://scheduling.test/"), new Uri("http://messaging.test/")));
            await using var db = Fixture.Base.CreateOwnerContext();
            await new CoordinateLifecycle(new EfLifecycleStore(db), failAfterBlock ? new LostAck(directory) : directory).RunAsync(op, default);
        }
        public async ValueTask DisposeAsync() { host?.Dispose(); await Fixture.DisposeAsync(); }
    }
    private sealed class LostAck(ILifecycleDirectory inner) : ILifecycleDirectory
    {
        public Task<bool> FreezeAsync(Engagement e, CancellationToken ct) => inner.FreezeAsync(e, ct);
        public async Task ApplyConversationAsync(Engagement e, bool close, CancellationToken ct)
        { await inner.ApplyConversationAsync(e, close, ct); throw new HttpRequestException("Lost acknowledgement."); }
    }
    private sealed class Tokens(SchedulingFixture f) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var form = await request.Content!.ReadAsStringAsync(ct);
            var scope = form.Contains("scheduling.engagement.freeze", StringComparison.Ordinal) ? "scheduling.engagement.freeze" : "messaging.conversation.lifecycle";
            Assert.Contains("client_id=inklink-engagements", form);
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { access_token = f.Base.Token("svc", "service", "inklink-engagements", scope) }) };
        }
    }
    private sealed class BackendRouter(SchedulingFixture f, ChatHost chat) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var invoker = new HttpMessageInvoker(request.RequestUri!.Host == "scheduling.test" ? f.Host.Server.CreateHandler() : chat.Server.CreateHandler());
            return invoker.SendAsync(request, ct);
        }
    }
    private sealed class ChatHost(SchedulingFixture f, string connection) : WebApplicationFactory<MessagingProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            foreach (var pair in new Dictionary<string, string?>
            {
                ["MessagingDatabase:ConnectionString"] = connection,
                ["Authentication:Authority"] = f.Base.Authority,
                ["Authentication:Audience"] = "inklink-api",
                ["Authentication:UserClients:0"] = f.Base.UserClient,
                ["Messaging:IdentityAddress"] = "http://identity.test/",
                ["Messaging:EngagementsAddress"] = "http://engagements.test/",
                ["Messaging:ClientSecret"] = "test-only"
            }) builder.UseSetting(pair.Key, pair.Value);
            builder.ConfigureTestServices(services =>
            {
                services.Configure<JwtBearerOptions>("Bearer", f.Base.ValidateTokens);
                services.AddHttpClient<InkLink.Messaging.Application.Accounts.IIdentityDirectory, InkLink.Messaging.Infrastructure.Integration.IdentityDirectory>()
                    .ConfigurePrimaryHttpMessageHandler(() => f.Base.Identity.Server.CreateHandler());
            });
        }
    }
}
