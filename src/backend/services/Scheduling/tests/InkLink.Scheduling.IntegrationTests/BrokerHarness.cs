using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace InkLink.Scheduling.IntegrationTests;

internal sealed class BrokerHarness : IAsyncDisposable
{
    private readonly string adminPassword = Guid.NewGuid().ToString("N");
    private readonly string publisherPassword = Guid.NewGuid().ToString("N");
    private readonly string consumerPassword = Guid.NewGuid().ToString("N");
    private readonly IContainer container;
    public BrokerHarness()
    {
        container = new ContainerBuilder("rabbitmq:3.13.7-alpine")
            .WithEnvironment("RABBITMQ_DEFAULT_USER", "inklink")
            .WithEnvironment("RABBITMQ_DEFAULT_PASS", adminPassword)
            .WithEnvironment("RABBITMQ_PUBLISHER_PASS", publisherPassword)
            .WithEnvironment("RABBITMQ_CONSUMER_PASS", consumerPassword)
            .WithEnvironment("RABBITMQ_SCHEDULING_PUBLISHER_PASS", publisherPassword)
            .WithEnvironment("RABBITMQ_RESERVATION_CONSUMER_PASS", consumerPassword)
            .WithEnvironment("RABBITMQ_SERVER_ADDITIONAL_ERL_ARGS", "+S 2:2")
            .WithEnvironment("RABBITMQ_CTL_ERL_ARGS", "+S 1:1")
            .WithPortBinding(5672, true)
            .WithResourceMapping(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Broker", "configure-broker.sh")), "/opt/inklink/configure-broker.sh")
            .WithResourceMapping(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Broker", "broker-definitions.json")), "/opt/inklink/broker-definitions.json")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Server startup complete"))
            .Build();
    }
    public string PublisherUri => Address("inklink_scheduling_publisher", publisherPassword);
    public string ConsumerUri => Address("inklink_reservation_consumer", consumerPassword);
    public ConnectionFactory Admin => new() { Uri = new Uri(Address("inklink", adminPassword)), AutomaticRecoveryEnabled = false };
    private string Address(string user, string password) => $"amqp://{user}:{password}@{container.Hostname}:{container.GetMappedPublicPort(5672)}/";
    public async Task InitializeAsync()
    {
        await container.StartAsync();
        for (var i = 0; i < 2; i++)
        {
            var result = await container.ExecAsync(["sh", "/opt/inklink/configure-broker.sh"]);
            Assert.True(result.ExitCode == 0, "Broker provisioning failed: " + result.Stderr);
        }
    }
    public Task StopAsync() => ApplicationAsync("stop_app");
    public Task StartAsync() => ApplicationAsync("start_app");
    private async Task ApplicationAsync(string command)
    {
        var result = await container.ExecAsync(["rabbitmqctl", command]);
        Assert.True(result.ExitCode == 0, "RabbitMQ application operation failed: " + result.Stderr);
    }
    public async ValueTask DisposeAsync() => await container.DisposeAsync();

    public void VerifyRolePermissions()
    {
        using var publisher = new ConnectionFactory { Uri = new(PublisherUri) }.CreateConnection();
        using var consumer = new ConnectionFactory { Uri = new(ConsumerUri) }.CreateConnection();
        using (var channel = publisher.CreateModel())
            Assert.Equal(403, Assert.Throws<OperationInterruptedException>(() => channel.BasicGet("inklink.engagements.reservations.v1", true)).ShutdownReason.ReplyCode);
        using (var channel = publisher.CreateModel())
            Assert.Equal(403, Assert.Throws<OperationInterruptedException>(() => channel.QueueDeclare("forbidden", true, false, false)).ShutdownReason.ReplyCode);
        using (var channel = consumer.CreateModel())
            Assert.Equal(403, Assert.Throws<OperationInterruptedException>(() => channel.QueueDeclare("forbidden", true, false, false)).ShutdownReason.ReplyCode);
        using (var channel = consumer.CreateModel())
        {
            channel.ConfirmSelect();
            Assert.ThrowsAny<Exception>(() =>
            {
                channel.BasicPublish("inklink.scheduling.events.v1", "scheduling.reservation-confirmed.v1", null, new byte[] { 1 });
                channel.WaitForConfirmsOrDie(TimeSpan.FromSeconds(5));
            });
            Assert.Equal(403, channel.CloseReason!.ReplyCode);
        }
        using (var channel = publisher.CreateModel())
        {
            channel.ConfirmSelect();
            Assert.ThrowsAny<Exception>(() =>
            {
                channel.BasicPublish("", "inklink.engagements.reservations.invalid.v1", null, new byte[] { 1 });
                channel.WaitForConfirmsOrDie(TimeSpan.FromSeconds(5));
            });
            Assert.Equal(403, channel.CloseReason!.ReplyCode);
        }
    }

    public void Publish(byte[] payload)
    {
        using var connection = new ConnectionFactory { Uri = new(PublisherUri), AutomaticRecoveryEnabled = false }.CreateConnection();
        using var channel = connection.CreateModel();
        channel.ConfirmSelect();
        var properties = channel.CreateBasicProperties(); properties.Persistent = true; properties.ContentType = "application/json";
        channel.BasicPublish("inklink.scheduling.events.v1", "scheduling.reservation-confirmed.v1", true, properties, payload);
        channel.WaitForConfirmsOrDie(TimeSpan.FromSeconds(5));
    }

    public static async Task UntilAsync(Func<Task<bool>> predicate, string reason, int seconds = 120)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            if (await predicate()) return;
            await Task.Delay(300);
        }
        Assert.Fail(reason);
    }
}
