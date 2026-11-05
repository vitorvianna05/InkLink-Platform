using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;
using InkLink.Catalog.Application.Profiles;
using RabbitMQ.Client;

namespace InkLink.Catalog.Api.Extensions;

internal static class ProfilePhotoConsumerExtensions
{
    /// <summary>Sem broker configurado (testes isolados), a projeção é alimentada pelos próprios testes.</summary>
    public static IServiceCollection AddProfilePhotoConsumer(this IServiceCollection services, IConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(configuration["Broker:Uri"])) services.AddHostedService<ProfilePhotoConsumer>();
        return services;
    }
}

internal static class ProfilePhotoTelemetry
{
    public const string Name = "InkLink.Catalog.ProfilePhotos";
    public static readonly ActivitySource Activities = new(Name);
    private static readonly Meter Meter = new(Name);
    private static readonly Counter<long> Deliveries = Meter.CreateCounter<long>("inklink.catalog.profile_photos.deliveries");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("inklink.catalog.profile_photos.processing.duration", "s");
    public static void Record(string outcome, long started)
    {
        var tags = new TagList { { "outcome", outcome } };
        Deliveries.Add(1, tags);
        Duration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds, tags);
    }
}

/// <summary>
/// Consome identity.profile-photo-changed.v1 e aciona <see cref="ProjectArtistPhoto"/> (padrão dos consumidores de P05/P06):
/// evento inválido vai para a fila de inválidos; falha transitória volta pela exchange de nova tentativa
/// com contador, e a décima falha segue para a fila de inválidos para diagnóstico e reprocessamento controlado.
/// </summary>
internal sealed class ProfilePhotoConsumer(IServiceScopeFactory scopes, IConfiguration configuration, IHostEnvironment environment, ILogger<ProfilePhotoConsumer> logger) : BackgroundService
{
    public const string Queue = "inklink.catalog.profile-photos.v1";
    public const string RetryExchange = "inklink.catalog.profile-photos.retry.v1";
    private const int MaxAttempts = 10;
    private const string AttemptHeader = "inklink-processing-attempt";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { while (await ConsumeOneAsync(stoppingToken)) { } }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogWarning("Profile photo consumer cycle failed: {FailureType}", exception.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>Processa uma entrega; devolve falso quando a fila está vazia ou a entrega volta para nova tentativa.</summary>
    private async Task<bool> ConsumeOneAsync(CancellationToken ct)
    {
        var broker = new Uri(configuration["Broker:Uri"] ?? throw new InvalidOperationException("Broker URI missing."));
        if (broker.Scheme != "amqps" && !(broker.Scheme == "amqp" && (environment.IsDevelopment() || environment.IsEnvironment("Testing"))))
            throw new InvalidOperationException("Broker requires TLS outside local/test environments.");
        var factory = new ConnectionFactory { Uri = broker, RequestedConnectionTimeout = TimeSpan.FromSeconds(5), AutomaticRecoveryEnabled = false };
        using var connection = factory.CreateConnection("inklink-catalog-photos");
        using var channel = connection.CreateModel();
        var delivery = channel.BasicGet(Queue, autoAck: false);
        if (delivery is null) return false;
        var started = Stopwatch.GetTimestamp();
        var attempt = 1;
        ProfilePhotoEnvelope? envelope = null;
        Activity? activity = null;
        var outcome = "transport_failure";
        try
        {
            if (delivery.Body.Length > 16 * 1024) throw new InvalidPhotoEvent("Envelope too large.");
            envelope = JsonSerializer.Deserialize<ProfilePhotoEnvelope>(delivery.Body.Span, Json) ?? throw new InvalidPhotoEvent("Envelope required.");
            attempt = ReadAttempt(delivery.BasicProperties);
            var parent = Header(delivery.BasicProperties, "traceparent") ?? envelope.TraceParent;
            ActivityContext.TryParse(parent, null, isRemote: true, out var context);
            activity = ProfilePhotoTelemetry.Activities.StartActivity("profile-photo.project", ActivityKind.Consumer, context);
            activity?.SetTag("messaging.message.id", envelope.MessageId.ToString());
            activity?.SetTag("messaging.operation.attempt", attempt);
            activity?.SetTag("inklink.correlation_id", envelope.CorrelationId);
            using var logScope = logger.BeginScope(new Dictionary<string, object?>
            {
                ["eventId"] = envelope.MessageId,
                ["correlationId"] = envelope.CorrelationId,
                ["traceId"] = activity?.TraceId.ToString(),
                ["attempt"] = attempt,
            });
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ProjectArtistPhoto>().ExecuteAsync(envelope, ct);
            channel.BasicAck(delivery.DeliveryTag, multiple: false);
            outcome = "processed";
            activity?.SetStatus(ActivityStatusCode.Ok);
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidPhotoEvent)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            channel.BasicReject(delivery.DeliveryTag, requeue: false);
            outcome = "invalid";
            logger.LogWarning("Invalid profile photo event {EventId} rejected: {FailureType}", envelope?.MessageId, exception.GetType().Name);
            return true;
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
                // A cópia persistente é confirmada antes de reconhecer a original; numa falha de transporte,
                // o descarte do canal devolve a original à fila.
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
                channel.BasicPublish(RetryExchange, "retry", true, properties, delivery.Body);
                channel.WaitForConfirmsOrDie(TimeSpan.FromSeconds(5));
                if (returned) throw new InvalidOperationException("Retry event was unroutable.");
                channel.BasicAck(delivery.DeliveryTag, multiple: false);
                outcome = "retry";
            }
            logger.LogWarning("Profile photo event {EventId} with correlation {CorrelationId} attempt {Attempt}: {Outcome}; {FailureType}",
                envelope?.MessageId, envelope?.CorrelationId, attempt, outcome, exception.GetType().Name);
            return false;
        }
        finally
        {
            ProfilePhotoTelemetry.Record(outcome, started);
            activity?.Dispose();
        }
    }

    private static int ReadAttempt(IBasicProperties properties)
    {
        if (properties.Headers is null || !properties.Headers.TryGetValue(AttemptHeader, out var value)) return 1;
        if (value is int number && number is >= 1 and <= MaxAttempts) return number;
        throw new InvalidPhotoEvent("Invalid processing attempt.");
    }

    private static string? Header(IBasicProperties properties, string name) =>
        properties.Headers is { } headers && headers.TryGetValue(name, out var value)
            ? value is byte[] bytes ? Encoding.UTF8.GetString(bytes) : value as string
            : null;
}
