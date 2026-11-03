using System.Text.Json;
using InkLink.Messaging.Application.Conversations;
using InkLink.Messaging.Domain;
using RabbitMQ.Client;

namespace InkLink.Messaging.Api.Extensions;

internal sealed class ConversationConsumer(IServiceScopeFactory scopes, IConfiguration configuration, IHostEnvironment environment, ILogger<ConversationConsumer> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ConsumeOneAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogWarning("Conversation consumer cycle failed: {FailureType}", exception.GetType().Name); }
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }

    private async Task ConsumeOneAsync(CancellationToken ct)
    {
        var uri = configuration["Broker:Uri"] ?? throw new InvalidOperationException("Broker URI missing.");
        var broker = new Uri(uri);
        if (broker.Scheme != "amqps" && !(broker.Scheme == "amqp" && (environment.IsDevelopment() || environment.IsEnvironment("Testing"))))
            throw new InvalidOperationException("Broker requires TLS outside local/test environments.");
        var factory = new ConnectionFactory { Uri = broker, RequestedConnectionTimeout = TimeSpan.FromSeconds(5), AutomaticRecoveryEnabled = false };
        using var connection = factory.CreateConnection("inklink-messaging-consumer");
        using var channel = connection.CreateModel();
        var delivery = channel.BasicGet("inklink.messaging.opened.v1", autoAck: false);
        if (delivery is null) return;
        try
        {
            if (delivery.Body.Length > 65536) throw new InvalidConversationData("event", "Envelope too large.");
            var envelope = JsonSerializer.Deserialize<EventEnvelope>(delivery.Body.Span, Json) ?? throw new InvalidConversationData("event", "Envelope required.");
            using var scope = scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<OpenConversation>().ExecuteAsync(envelope, ct);
            logger.LogInformation("Integration event {EventId} for engagement {EngagementId} processed with correlation {CorrelationId}",
                envelope.MessageId, envelope.Data.EngagementId, envelope.CorrelationId);
            channel.BasicAck(delivery.DeliveryTag, multiple: false);
        }
        catch (Exception exception) when (exception is JsonException or InvalidConversationData)
        {
            logger.LogWarning("Invalid integration event rejected: {FailureType}", exception.GetType().Name);
            channel.BasicReject(delivery.DeliveryTag, requeue: false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("Integration event processing delayed: {FailureType}", exception.GetType().Name);
            channel.BasicNack(delivery.DeliveryTag, multiple: false, requeue: true);
        }
    }
}
