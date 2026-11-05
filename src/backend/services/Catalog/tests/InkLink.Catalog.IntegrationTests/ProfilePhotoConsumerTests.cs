using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using InkLink.Catalog.Application.Profiles;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;

namespace InkLink.Catalog.IntegrationTests;

/// <summary>
/// Consumidor da foto do artista contra RabbitMQ real, com as definições e os usuários versionados:
/// orçamento de tentativas, quarentena na fila de inválidos e propagação do rastreamento (arquitetura §7.3 e §10).
/// </summary>
[Collection("catalog")]
public sealed class ProfilePhotoConsumerTests(CatalogFixture fixture)
{
    private const string Invalid = "inklink.catalog.profile-photos.invalid.v1";

    [Fact]
    public async Task Transient_failures_are_retried_ten_times_then_quarantined_with_the_trace_preserved()
    {
        await using var broker = await PhotoBroker.StartAsync();
        using var scenario = await fixture.CreateAsync();
        var projection = new FailingProjection();
        var parent = new ActivityContext(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded);
        var envelope = new ProfilePhotoEnvelope(Guid.CreateVersion7(), ProjectArtistPhoto.EventType, 1, DateTimeOffset.UtcNow, parent.TraceId.ToString(), null,
            new(Guid.CreateVersion7(), 1, "https://localhost:5173/media/inklink-identity-media/profiles/a/1/512.webp", "https://localhost:5173/media/inklink-identity-media/profiles/a/1/128.webp"),
            $"00-{parent.TraceId}-{parent.SpanId}-01");
        var payload = JsonSerializer.SerializeToUtf8Bytes(envelope, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var outcomes = new ConcurrentBag<string>();
        var spans = new ConcurrentBag<Activity>();
        using var listener = Listen(spans);
        using var meters = Measure(outcomes);

        broker.Publish(payload);
        using (var host = scenario.Host.WithWebHostBuilder(builder => builder.UseSetting("Broker:Uri", broker.ConsumerUri)
            .ConfigureTestServices(services => services.AddScoped<IArtistPhotoProjection>(_ => projection))))
        {
            using var client = host.CreateClient();
            await PhotoBroker.UntilAsync(() => broker.Count(Invalid) == 1, "Retry budget was not exhausted.");
        }

        var quarantined = broker.Get(Invalid)!;
        Assert.Equal(payload, quarantined.Body.ToArray());
        Assert.Equal(10, quarantined.BasicProperties.Headers["inklink-processing-attempt"]);
        Assert.Equal(10, projection.Failures);
        Assert.Contains("retry", outcomes);
        Assert.Contains("quarantined", outcomes);
        Assert.Equal(0u, broker.Count("inklink.catalog.profile-photos.v1"));
        Assert.All(spans.Where(span => span.OperationName == "profile-photo.project"), span => Assert.Equal(parent.TraceId, span.TraceId));
        Assert.Equal(10, spans.Count(span => span.OperationName == "profile-photo.project"));
    }

    [Fact]
    public async Task Invalid_events_go_straight_to_the_invalid_queue_without_touching_the_projection()
    {
        await using var broker = await PhotoBroker.StartAsync();
        using var scenario = await fixture.CreateAsync();
        var projection = new FailingProjection();
        var outcomes = new ConcurrentBag<string>();
        using var meters = Measure(outcomes);

        broker.Publish(Encoding.UTF8.GetBytes("""{"messageId":"01a1a6ba-6898-7271-9134-607a74e040d3","type":"identity.other.v1","schemaVersion":1}"""));
        using (var host = scenario.Host.WithWebHostBuilder(builder => builder.UseSetting("Broker:Uri", broker.ConsumerUri)
            .ConfigureTestServices(services => services.AddScoped<IArtistPhotoProjection>(_ => projection))))
        {
            using var client = host.CreateClient();
            await PhotoBroker.UntilAsync(() => broker.Count(Invalid) == 1, "Invalid event was not rejected.");
        }

        Assert.Equal(0, projection.Failures);
        Assert.Equal(["invalid"], outcomes.Distinct());
    }

    private static ActivityListener Listen(ConcurrentBag<Activity> spans)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "InkLink.Catalog.ProfilePhotos",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = spans.Add,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static MeterListener Measure(ConcurrentBag<string> outcomes)
    {
        var meters = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == "InkLink.Catalog.ProfilePhotos") listener.EnableMeasurementEvents(instrument);
            },
        };
        meters.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            foreach (var tag in tags) if (tag.Key == "outcome" && tag.Value is string outcome) outcomes.Add(outcome);
        });
        meters.Start();
        return meters;
    }

    private sealed class FailingProjection : IArtistPhotoProjection
    {
        private int failures;
        public int Failures => failures;
        public Task ApplyAsync(ArtistPhotoChange change, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref failures);
            throw new TimeoutException("Projection store unavailable.");
        }
    }

    /// <summary>RabbitMQ com as definições e os usuários versionados; publica como a Identity.</summary>
    private sealed class PhotoBroker : IAsyncDisposable
    {
        private static string Secret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        private readonly string admin = Secret();
        private readonly string publisher = Secret();
        private readonly string consumer = Secret();
        private readonly IContainer container;

        private PhotoBroker()
        {
            static byte[] Asset(string name) => Encoding.UTF8.GetBytes(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, name)).Replace("\r\n", "\n", StringComparison.Ordinal));
            container = new ContainerBuilder("rabbitmq:3.13.7-alpine")
                .WithEnvironment("RABBITMQ_DEFAULT_USER", "inklink")
                .WithEnvironment("RABBITMQ_DEFAULT_PASS", admin)
                .WithEnvironment("RABBITMQ_PUBLISHER_PASS", Secret())
                .WithEnvironment("RABBITMQ_CONSUMER_PASS", Secret())
                .WithEnvironment("RABBITMQ_IDENTITY_PUBLISHER_PASS", publisher)
                .WithEnvironment("RABBITMQ_CATALOG_CONSUMER_PASS", consumer)
                .WithEnvironment("RABBITMQ_SERVER_ADDITIONAL_ERL_ARGS", "+S 2:2")
                .WithEnvironment("RABBITMQ_CTL_ERL_ARGS", "+S 1:1")
                .WithPortBinding(5672, true)
                .WithResourceMapping(Asset("configure-broker.sh"), "/opt/inklink/configure-broker.sh")
                .WithResourceMapping(Asset("broker-definitions.json"), "/opt/inklink/broker-definitions.json")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Server startup complete"))
                .Build();
        }

        private string Uri(string user, string password) => $"amqp://{user}:{password}@{container.Hostname}:{container.GetMappedPublicPort(5672)}/";
        public string ConsumerUri => Uri("inklink_catalog_consumer", consumer);

        public static async Task<PhotoBroker> StartAsync()
        {
            var broker = new PhotoBroker();
            await broker.container.StartAsync();
            var users = await broker.container.ExecAsync(["sh", "/opt/inklink/configure-broker.sh"]);
            Assert.True(users.ExitCode == 0, users.Stderr);
            return broker;
        }

        /// <summary>Publica com o usuário da Identity, que só escreve na exchange de eventos da Identity.</summary>
        public void Publish(byte[] payload)
        {
            using var connection = new ConnectionFactory { Uri = new System.Uri(Uri("inklink_identity_publisher", publisher)) }.CreateConnection();
            using var channel = connection.CreateModel();
            channel.ConfirmSelect();
            var properties = channel.CreateBasicProperties();
            properties.Persistent = true;
            properties.ContentType = "application/json";
            channel.BasicPublish("inklink.identity.events.v1", ProjectArtistPhoto.EventType, true, properties, payload);
            channel.WaitForConfirmsOrDie(TimeSpan.FromSeconds(5));
        }

        public uint Count(string queue)
        {
            using var connection = Admin();
            using var channel = connection.CreateModel();
            return channel.MessageCount(queue);
        }

        public BasicGetResult? Get(string queue)
        {
            using var connection = Admin();
            using var channel = connection.CreateModel();
            return channel.BasicGet(queue, autoAck: true);
        }

        private IConnection Admin() => new ConnectionFactory { Uri = new System.Uri(Uri("inklink", admin)) }.CreateConnection();

        public static async Task UntilAsync(Func<bool> condition, string message)
        {
            var deadline = DateTime.UtcNow.AddSeconds(90);
            while (DateTime.UtcNow < deadline)
            {
                if (condition()) return;
                await Task.Delay(250);
            }
            Assert.Fail(message);
        }

        public ValueTask DisposeAsync() => container.DisposeAsync();
    }
}
