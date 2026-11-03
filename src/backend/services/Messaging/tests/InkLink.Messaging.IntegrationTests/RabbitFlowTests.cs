using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using InkLink.Engagements.Application.Negotiations;
using InkLink.Messaging.Application.Conversations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;

namespace InkLink.Messaging.IntegrationTests;

[Collection("messaging")]
public sealed class RabbitFlowTests(MessagingFixture fixture)
{
    [Fact]
    public async Task Broker_outage_recovers_outbox_and_creates_one_shared_conversation()
    {
        var spans = new ConcurrentBag<Activity>();
        var measurements = new ConcurrentBag<string>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name is "InkLink.Engagements.Events" or "InkLink.Messaging.Events",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => spans.Add(activity)
        };
        ActivitySource.AddActivityListener(listener);
        using var meters = new MeterListener
        {
            InstrumentPublished = (instrument, meter) =>
            {
                if (instrument.Meter.Name is "InkLink.Engagements.Events" or "InkLink.Messaging.Events") meter.EnableMeasurementEvents(instrument);
            }
        };
        meters.SetMeasurementEventCallback<long>((instrument, value, tags, _) => measurements.Add(instrument.Name));
        meters.SetMeasurementEventCallback<double>((instrument, value, tags, _) => measurements.Add(instrument.Name));
        meters.Start();
        await using var broker = new BrokerHarness(); await broker.InitializeAsync(); broker.VerifyRolePermissions();
        var publisherUri = broker.PublisherUri; var consumerUri = broker.ConsumerUri;
        await broker.StopAsync();
        using var publisher = fixture.Base.NewHost().WithWebHostBuilder(builder => builder.UseSetting("Broker:Uri", publisherUri).UseSetting("Broker:EnableWorkers", "true"));
        using var consumer = fixture.Host.WithWebHostBuilder(builder => builder.UseSetting("Broker:Uri", consumerUri).UseSetting("Broker:EnableWorkers", "true"));
        using var catalog = fixture.Base.Catalog.CreateClient(); catalog.DefaultRequestHeaders.Authorization = new("Bearer", fixture.Base.Token("bob"));
        var published = await catalog.PutAsJsonAsync("/api/v1/artists/me", new { name = "Bob", studio = "EstÃºdio", city = "SÃ£o Paulo", biography = "PortfÃ³lio", styles = new[] { "Blackwork" }, startingPrice = 300 });
        Assert.True(published.IsSuccessStatusCode);
        var artist = (await published.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<Guid>();
        using var client = publisher.CreateClient(); client.DefaultRequestHeaders.Authorization = new("Bearer", fixture.Base.Token("alice"));
        using var conversations = consumer.CreateClient(); conversations.DefaultRequestHeaders.Authorization = new("Bearer", fixture.Base.Token("alice"));
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v2/engagements") { Content = JsonContent.Create(new RequestInput(artist, "Teste", "BraÃ§o", 10)) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var response = await client.SendAsync(request); Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var engagement = (await response.Content.ReadFromJsonAsync<EngagementView>())!;
        Guid eventId; string originalPayload;
        await using (var db = fixture.Base.CreateOwnerContext())
        {
            var entry = await db.Outbox.SingleAsync(e => e.EngagementId == engagement.Id);
            Assert.Null(entry.PublishedAt); eventId = entry.Id; originalPayload = entry.Payload;
        }
        await broker.StartAsync();
        await BrokerHarness.UntilAsync(async () =>
        {
            await using var db = fixture.CreateOwnerContext();
            return await db.Conversations.AnyAsync(c => c.EngagementId == engagement.Id);
        }, "Outbox did not recover after broker restart.");
        await BrokerHarness.UntilAsync(async () =>
        {
            await using var db = fixture.Base.CreateOwnerContext();
            return (await db.Outbox.SingleAsync(e => e.Id == eventId)).PublishedAt is not null;
        }, "Published event was not acknowledged durably.");
        ConversationView conversation;
        await using (var db = fixture.CreateOwnerContext())
        {
            var id = (await db.Conversations.SingleAsync(c => c.EngagementId == engagement.Id)).Id;
            conversation = (await conversations.GetFromJsonAsync<ConversationView>($"/api/v1/conversations/{id}"))!;
            Assert.Equal("Bob", conversation.Counterpart.DisplayName); Assert.Equal("Negotiating", conversation.EngagementState);
        }
        var producerSpan = Assert.Single(spans, span => span.Source.Name == "InkLink.Engagements.Events"
            && (string?)span.GetTagItem("messaging.message.id") == eventId.ToString() && span.Status == ActivityStatusCode.Ok);
        var consumerSpan = Assert.Single(spans, span => span.Source.Name == "InkLink.Messaging.Events"
            && (string?)span.GetTagItem("messaging.message.id") == eventId.ToString() && span.Status == ActivityStatusCode.Ok);
        Assert.Equal(producerSpan.TraceId, consumerSpan.TraceId);
        Assert.Equal(producerSpan.SpanId, consumerSpan.ParentSpanId);
        Assert.Contains("inklink.engagements.publications", measurements);
        Assert.Contains("inklink.engagements.outbox.age", measurements);
        Assert.Contains("inklink.messaging.deliveries", measurements);
        Assert.Contains("inklink.messaging.processing.duration", measurements);
        broker.Publish(Encoding.UTF8.GetBytes(originalPayload));
        // Change the event ID to exercise idempotency by engagement as well as Inbox deduplication.
        var repeated = JsonNode.Parse(originalPayload)!; var repeatedId = Guid.NewGuid(); repeated["messageId"] = repeatedId;
        broker.Publish(Encoding.UTF8.GetBytes(repeated.ToJsonString()));
        await BrokerHarness.UntilAsync(async () =>
        {
            await using var db = fixture.CreateOwnerContext(); return await db.Inbox.AnyAsync(i => i.Id == repeatedId);
        }, "Repeated opening event was not consumed.");
        await using (var db = fixture.CreateOwnerContext()) Assert.Equal(1, await db.Conversations.CountAsync(c => c.EngagementId == engagement.Id));
        var key = Guid.NewGuid();
        async Task<HttpResponseMessage> Send(string body)
        {
            var message = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/conversations/{conversation.Id}/messages") { Content = JsonContent.Create(new MessageInput(body)) };
            message.Headers.Add("Idempotency-Key", key.ToString()); return await conversations.SendAsync(message);
        }
        Assert.Equal(HttpStatusCode.Created, (await Send("ApÃ³s recuperaÃ§Ã£o")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send("ApÃ³s recuperaÃ§Ã£o")).StatusCode);
        using var bob = consumer.CreateClient(); bob.DefaultRequestHeaders.Authorization = new("Bearer", fixture.Base.Token("bob"));
        var artistView = (await bob.GetFromJsonAsync<ConversationView>($"/api/v1/conversations/{conversation.Id}"))!;
        Assert.Equal("alice", artistView.Counterpart.DisplayName); Assert.Equal("ApÃ³s recuperaÃ§Ã£o", artistView.LastMessage!.Body);
        var incompatible = JsonNode.Parse(originalPayload)!; incompatible["schemaVersion"] = 999;
        broker.Publish(Encoding.UTF8.GetBytes(incompatible.ToJsonString()));
        using var admin = broker.Admin.CreateConnection(); using var channel = admin.CreateModel();
        await BrokerHarness.UntilAsync(() => Task.FromResult(channel.MessageCount("inklink.messaging.invalid.v1") > 0), "Invalid event was not quarantined by RabbitMQ.");
        Assert.NotNull(channel.BasicGet("inklink.messaging.invalid.v1", true));
        await using (var db = fixture.Base.CreateOwnerContext()) Assert.Equal(originalPayload, (await db.Outbox.SingleAsync(e => e.Id == eventId)).Payload);
    }
}
