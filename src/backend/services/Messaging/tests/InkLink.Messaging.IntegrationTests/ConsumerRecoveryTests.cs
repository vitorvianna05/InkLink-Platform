using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using InkLink.Messaging.Application.Conversations;
using InkLink.Messaging.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InkLink.Messaging.IntegrationTests;

[Collection("messaging")]
public sealed class ConsumerRecoveryTests(MessagingFixture fixture)
{
    [Fact]
    public async Task Retry_budget_survives_host_restart_quarantines_and_allows_controlled_recovery()
    {
        await using var broker = new BrokerHarness();
        await broker.InitializeAsync(); broker.VerifyRolePermissions();
        var state = new FailureSwitch();
        var parent = new ActivityContext(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded);
        var traceParent = $"00-{parent.TraceId}-{parent.SpanId}-01";
        var envelope = new EventEnvelope(Guid.NewGuid(), "engagement.negotiation-opened.v1", 1, DateTimeOffset.UtcNow,
            parent.TraceId.ToString(), null, new(state.Engagement, Guid.NewGuid(), Guid.NewGuid(), 1, true), traceParent);
        var payload = JsonSerializer.SerializeToUtf8Bytes(envelope, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var outcomes = new ConcurrentBag<string>();
        var spans = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "InkLink.Messaging.Events",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => spans.Add(activity)
        };
        ActivitySource.AddActivityListener(listener);
        using var meters = new MeterListener
        {
            InstrumentPublished = (instrument, meter) =>
            {
                if (instrument.Meter.Name == "InkLink.Messaging.Events") meter.EnableMeasurementEvents(instrument);
            }
        };
        meters.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            foreach (var tag in tags) if (tag.Key == "outcome" && tag.Value is string outcome) outcomes.Add(outcome);
        });
        meters.Start();

        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<MessagingProgram> Host() =>
            fixture.Host.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Broker:Uri", broker.ConsumerUri).UseSetting("Broker:EnableWorkers", "true");
                builder.ConfigureTestServices(services => services.AddScoped<IConversationStore>(provider =>
                {
                    var proxy = DispatchProxy.Create<IConversationStore, FaultingStore>();
                    ((FaultingStore)(object)proxy).Initialize(new EfConversationStore(provider.GetRequiredService<MessagingDbContext>()), state);
                    return proxy;
                }));
            });
        broker.Publish(payload);
        using (var first = Host())
        {
            using var client = first.CreateClient();
            await BrokerHarness.UntilAsync(() => Task.FromResult(outcomes.Contains("retry")), "Retry was not confirmed.");
        }
        using (var second = Host())
        {
            using var client = second.CreateClient();
            using var connection = broker.Admin.CreateConnection(); using var channel = connection.CreateModel();
            await BrokerHarness.UntilAsync(() => Task.FromResult(channel.MessageCount("inklink.messaging.invalid.v1") == 1), "Retry budget was not exhausted.");
            var quarantined = channel.BasicGet("inklink.messaging.invalid.v1", false)!;
            Assert.Equal(payload, quarantined.Body.ToArray());
            Assert.Equal(10, quarantined.BasicProperties.Headers["inklink-processing-attempt"]);
            Assert.Equal(10, state.Failures);
            Assert.Contains("quarantined", outcomes);
            await using (var db = fixture.CreateOwnerContext())
            {
                Assert.False(await db.Inbox.AnyAsync(i => i.Id == envelope.MessageId));
                Assert.False(await db.Conversations.AnyAsync(c => c.EngagementId == state.Engagement));
            }
            // Other events continue while the failed event awaits operator intervention.
            var healthy = envelope with { MessageId = Guid.NewGuid(), Data = envelope.Data with { EngagementId = Guid.NewGuid() } };
            broker.Publish(JsonSerializer.SerializeToUtf8Bytes(healthy, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            await BrokerHarness.UntilAsync(async () =>
            {
                await using var db = fixture.CreateOwnerContext(); return await db.Inbox.AnyAsync(i => i.Id == healthy.MessageId);
            }, "A quarantined event blocked unrelated deliveries.");
            state.Recovered = true;
            // Operator preserves payload/ID, resets only transport retry metadata, confirms publish before removing diagnosis.
            broker.Publish(quarantined.Body.ToArray());
            channel.BasicAck(quarantined.DeliveryTag, false);
            await BrokerHarness.UntilAsync(async () =>
            {
                await using var db = fixture.CreateOwnerContext(); return await db.Inbox.AnyAsync(i => i.Id == envelope.MessageId);
            }, "Controlled recovery did not apply the event.");
        }
        Assert.Contains("processed", outcomes);
        var eventSpans = spans.Where(span => (string?)span.GetTagItem("messaging.message.id") == envelope.MessageId.ToString()).ToArray();
        Assert.True(eventSpans.Length >= 11);
        Assert.All(eventSpans, span => Assert.Equal(parent.TraceId, span.TraceId));
        Assert.Contains(eventSpans, span => span.ParentSpanId == parent.SpanId);
        Assert.Contains(eventSpans, span => span.Status == ActivityStatusCode.Error);
        Assert.Contains(eventSpans, span => span.Status == ActivityStatusCode.Ok);
    }

    [Fact]
    public async Task Unroutable_retry_preserves_the_unacknowledged_original()
    {
        await using var broker = new BrokerHarness(); await broker.InitializeAsync();
        using var connection = broker.Admin.CreateConnection(); using var channel = connection.CreateModel();
        channel.QueueUnbind("inklink.messaging.opened.v1", "inklink.messaging.retry.v1", "retry", null);
        var state = new FailureSwitch();
        var envelope = new EventEnvelope(Guid.NewGuid(), "engagement.negotiation-opened.v1", 1, DateTimeOffset.UtcNow,
            Guid.NewGuid().ToString("N"), null, new(state.Engagement, Guid.NewGuid(), Guid.NewGuid(), 1, true));
        var payload = JsonSerializer.SerializeToUtf8Bytes(envelope, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        broker.Publish(payload);
        using (var host = fixture.Host.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Broker:Uri", broker.ConsumerUri).UseSetting("Broker:EnableWorkers", "true");
            builder.ConfigureTestServices(services => services.AddScoped<IConversationStore>(provider =>
            {
                var proxy = DispatchProxy.Create<IConversationStore, FaultingStore>();
                ((FaultingStore)(object)proxy).Initialize(new EfConversationStore(provider.GetRequiredService<MessagingDbContext>()), state);
                return proxy;
            }));
        }))
        {
            using var client = host.CreateClient();
            await BrokerHarness.UntilAsync(() => Task.FromResult(Volatile.Read(ref state.Failures) > 0), "Fault was not injected.");
        }
        await BrokerHarness.UntilAsync(() => Task.FromResult(channel.MessageCount("inklink.messaging.opened.v1") == 1), "Original delivery was lost after a returned retry.");
        var original = channel.BasicGet("inklink.messaging.opened.v1", false)!;
        Assert.Equal(payload, original.Body.ToArray());
        Assert.False(original.BasicProperties.Headers?.ContainsKey("inklink-processing-attempt") ?? false);
        Assert.Equal(0u, channel.MessageCount("inklink.messaging.invalid.v1"));
        await using var database = fixture.CreateOwnerContext();
        Assert.False(await database.Inbox.AnyAsync(row => row.Id == envelope.MessageId));
        channel.BasicAck(original.DeliveryTag, false);
    }

    public sealed class FailureSwitch
    {
        public Guid Engagement { get; } = Guid.NewGuid();
        public volatile bool Recovered;
        public int Failures;
    }

    public class FaultingStore : DispatchProxy
    {
        private IConversationStore inner = null!;
        private FailureSwitch state = null!;
        public void Initialize(IConversationStore store, FailureSwitch failure) { inner = store; state = failure; }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == nameof(IConversationStore.ApplyOpeningAsync)
                && args![0] is ConversationOpening opening && opening.Conversation.EngagementId == state.Engagement && !state.Recovered)
            {
                Interlocked.Increment(ref state.Failures);
                return Task.FromException(new DbUpdateException("Injected permanent processing failure."));
            }
            try { return method.Invoke(inner, args); }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            { ExceptionDispatchInfo.Capture(exception.InnerException).Throw(); throw; }
        }
    }
}
