using System.Diagnostics;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace InkLink.Identity.Infrastructure.Messaging;

/// <summary>Publica os eventos de foto do Outbox com confirmação do broker (padrão de P05).</summary>
public sealed class OutboxWorker(IServiceScopeFactory scopes, IConfiguration configuration, IHostEnvironment environment, TimeProvider time, ILogger<OutboxWorker> logger) : BackgroundService
{
    public const string Exchange = "inklink.identity.events.v1";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { while (await PublishOneAsync(stoppingToken)) { } }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogWarning("Identity outbox cycle failed: {FailureType}", exception.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromSeconds(2), time, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>Publica o próximo evento pendente; devolve falso quando não há evento publicado.</summary>
    public async Task<bool> PublishOneAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var now = time.GetUtcNow();
        var pending = await database.Outbox
            .FromSql($"SELECT * FROM identity.outbox WHERE \"PublishedAt\" IS NULL AND \"QuarantinedAt\" IS NULL AND (\"NextAttemptAt\" IS NULL OR \"NextAttemptAt\" <= {now}) ORDER BY \"CreatedAt\" LIMIT 1 FOR UPDATE SKIP LOCKED")
            .SingleOrDefaultAsync(cancellationToken);
        if (pending is null) { await transaction.CommitAsync(cancellationToken); return false; }
        var started = Stopwatch.GetTimestamp();
        using var activity = EventTelemetry.Start(pending.Payload);
        activity?.SetTag("messaging.message.id", pending.Id.ToString());
        activity?.SetTag("messaging.operation.attempt", pending.Attempts + 1);
        try
        {
            var broker = new Uri(configuration["Broker:Uri"] ?? throw new InvalidOperationException("Broker URI missing."));
            if (broker.Scheme != "amqps" && !(broker.Scheme == "amqp" && (environment.IsDevelopment() || environment.IsEnvironment("Testing"))))
                throw new InvalidOperationException("Broker requires TLS outside local/test environments.");
            var factory = new ConnectionFactory { Uri = broker, RequestedConnectionTimeout = TimeSpan.FromSeconds(5), AutomaticRecoveryEnabled = false };
            using var connection = factory.CreateConnection("inklink-identity-outbox");
            using var channel = connection.CreateModel();
            channel.ConfirmSelect();
            var properties = channel.CreateBasicProperties();
            properties.Persistent = true;
            properties.ContentType = "application/json";
            properties.MessageId = pending.Id.ToString();
            if (activity?.Id is { } traceParent) properties.Headers = new Dictionary<string, object> { ["traceparent"] = traceParent };
            var returned = false;
            channel.BasicReturn += (_, _) => returned = true;
            channel.BasicPublish(Exchange, pending.Type, mandatory: true, properties, Encoding.UTF8.GetBytes(pending.Payload));
            channel.WaitForConfirmsOrDie(TimeSpan.FromSeconds(5));
            if (returned) throw new InvalidOperationException("Broker returned an unroutable event.");
            pending.PublishedAt = now;
            pending.LastError = null;
            pending.NextAttemptAt = null;
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            pending.RecordFailure(exception.GetType().Name, now);
            logger.LogWarning("Identity outbox publish failed for {EventId}: {FailureType}; attempts {Attempts}", pending.Id, pending.LastError, pending.Attempts);
        }
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        EventTelemetry.Record(pending.PublishedAt.HasValue ? "published" : pending.QuarantinedAt.HasValue ? "quarantined" : "retry", started, pending.CreatedAt, now);
        return pending.PublishedAt is not null;
    }
}
