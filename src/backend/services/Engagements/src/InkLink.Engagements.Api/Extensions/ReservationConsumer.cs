using System.Diagnostics;
using System.Text;
using System.Text.Json;
using InkLink.Engagements.Application.Scheduling;
using InkLink.Engagements.Domain;
using RabbitMQ.Client;

namespace InkLink.Engagements.Api.Extensions;

internal sealed class ReservationConsumer(IServiceScopeFactory scopes, IConfiguration configuration, IHostEnvironment environment, ILogger<ReservationConsumer> logger) : BackgroundService
{
    private const int MaxAttempts = 10;
    private const string AttemptHeader = "inklink-processing-attempt";
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
        var broker = new Uri(configuration["Broker:ReservationConsumerUri"] ?? throw new InvalidOperationException("Broker URI missing."));
        if (broker.Scheme != "amqps" && !(broker.Scheme == "amqp" && (environment.IsDevelopment() || environment.IsEnvironment("Testing"))))
            throw new InvalidOperationException("Broker requires TLS outside local/test environments.");
        var factory = new ConnectionFactory { Uri = broker, RequestedConnectionTimeout = TimeSpan.FromSeconds(5), AutomaticRecoveryEnabled = false };
        using var connection = factory.CreateConnection("inklink-engagements-reservation-consumer");
        using var channel = connection.CreateModel();
        var delivery = channel.BasicGet("inklink.engagements.reservations.v1", autoAck: false);
        if (delivery is null) return;
        var started = Stopwatch.GetTimestamp();
        var attempt = 1;
        ReservationEnvelope? envelope = null;
        Activity? activity = null;
        var outcome = "transport_failure";
        try
        {
            if (delivery.Body.Length > 65536) throw new InvalidEngagementData("event", "Envelope too large.");
            envelope = JsonSerializer.Deserialize<ReservationEnvelope>(delivery.Body.Span, Json) ?? throw new InvalidEngagementData("event", "Envelope required.");
            attempt = ReadAttempt(delivery.BasicProperties);
            var parent = Header(delivery.BasicProperties, "traceparent") ?? envelope.TraceParent;
            ActivityContext.TryParse(parent, null, isRemote: true, out var context);
            activity = ReservationTelemetry.Activities.StartActivity("reservation.record", ActivityKind.Consumer, context);
            activity?.SetTag("messaging.message.id", envelope.MessageId.ToString());
            activity?.SetTag("messaging.operation.attempt", attempt);
            activity?.SetTag("inklink.correlation_id", envelope.CorrelationId);
            using var logScope = logger.BeginScope(new Dictionary<string, object?>
            {
                ["eventId"] = envelope.MessageId,
                ["correlationId"] = envelope.CorrelationId,
                ["traceId"] = activity?.TraceId.ToString(),
                ["attempt"] = attempt
            });
            using var scope = scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<RecordReservation>().ExecuteAsync(envelope, ct);
            channel.BasicAck(delivery.DeliveryTag, multiple: false);
            outcome = "processed";
            activity?.SetStatus(ActivityStatusCode.Ok);
            logger.LogInformation("Integration event for engagement {EngagementId} processed", envelope.Data.EngagementId);
        }
        catch (Exception exception) when (exception is JsonException or InvalidEngagementData)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            channel.BasicReject(delivery.DeliveryTag, requeue: false);
            outcome = "invalid";
            logger.LogWarning("Invalid integration event {EventId} rejected: {FailureType}", envelope?.MessageId, exception.GetType().Name);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            if (attempt >= MaxAttempts)
            {
                channel.BasicReject(delivery.DeliveryTag, requeue: false);
                outcome = "quarantined";
            }
            else
            {
                // Confirm the persistent replacement before acknowledging the original.
                // On transport failure channel disposal returns the unacknowledged original.
                var properties = channel.CreateBasicProperties();
                properties.Persistent = true;
                properties.ContentType = delivery.BasicProperties.ContentType;
                properties.MessageId = delivery.BasicProperties.MessageId;
                properties.Headers = delivery.BasicProperties.Headers is { } headers ? new Dictionary<string, object>(headers) : new();
                properties.Headers[AttemptHeader] = attempt + 1;
                properties.Headers["inklink-last-failure"] = exception.GetType().Name;
                if (activity?.Id is { } traceParent) properties.Headers["traceparent"] = traceParent;
                var returned = false;
                channel.BasicReturn += (_, _) => returned = true;
                channel.ConfirmSelect();
                channel.BasicPublish("inklink.engagements.reservations.retry.v1", "retry", true, properties, delivery.Body);
                channel.WaitForConfirmsOrDie(TimeSpan.FromSeconds(5));
                if (returned) throw new InvalidOperationException("Retry event was unroutable.");
                channel.BasicAck(delivery.DeliveryTag, multiple: false);
                outcome = "retry";
            }
            logger.LogWarning("Integration event {EventId} with correlation {CorrelationId} attempt {Attempt}: {Outcome}; {FailureType}",
                envelope?.MessageId, envelope?.CorrelationId, attempt, outcome, exception.GetType().Name);
        }
        finally
        {
            ReservationTelemetry.Record(outcome, started);
            activity?.Dispose();
        }
    }

    private static int ReadAttempt(IBasicProperties properties)
    {
        if (properties.Headers is null || !properties.Headers.TryGetValue(AttemptHeader, out var value)) return 1;
        if (value is int number && number is >= 1 and <= MaxAttempts) return number;
        throw new InvalidEngagementData("attempt", "Invalid processing attempt.");
    }

    private static string? Header(IBasicProperties properties, string name) =>
        properties.Headers is { } headers && headers.TryGetValue(name, out var value)
            ? value is byte[] bytes ? Encoding.UTF8.GetString(bytes) : value as string
            : null;
}
