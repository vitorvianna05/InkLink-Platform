using System.Diagnostics;
using System.Text;
using InkLink.Scheduling.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace InkLink.Scheduling.Infrastructure.Messaging;

public sealed class OutboxWorker(IServiceScopeFactory scopes, IConfiguration configuration, IHostEnvironment environment, ILogger<OutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var database = scope.ServiceProvider.GetRequiredService<SchedulingDbContext>();
                await PublishOneAsync(database, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogWarning("Outbox cycle failed: {FailureType}", exception.GetType().Name); }
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }

    private async Task PublishOneAsync(SchedulingDbContext database, CancellationToken ct)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(ct);
        var pending = await database.Outbox.FromSqlRaw("SELECT * FROM scheduling.outbox WHERE \"PublishedAt\" IS NULL AND \"QuarantinedAt\" IS NULL AND (\"NextAttemptAt\" IS NULL OR \"NextAttemptAt\"<=now()) ORDER BY \"CreatedAt\",\"Id\" LIMIT 1 FOR UPDATE SKIP LOCKED").SingleOrDefaultAsync(ct);
        if (pending is null) { await transaction.CommitAsync(ct); return; }
        var started = Stopwatch.GetTimestamp();
        Activity? activity = null;
        try
        {
            activity = EventTelemetry.Start(pending.Payload);
            activity?.SetTag("messaging.message.id", pending.Id.ToString());
            var uri = configuration["Broker:PublisherUri"] ?? throw new InvalidOperationException("Broker URI missing.");
            var broker = new Uri(uri);
            if (broker.Scheme != "amqps" && !(broker.Scheme == "amqp" && (environment.IsDevelopment() || environment.IsEnvironment("Testing"))))
                throw new InvalidOperationException("Broker requires TLS outside local/test environments.");
            var factory = new ConnectionFactory { Uri = broker, RequestedConnectionTimeout = TimeSpan.FromSeconds(5), AutomaticRecoveryEnabled = false };
            using var connection = factory.CreateConnection("inklink-scheduling-outbox");
            using var channel = connection.CreateModel();
            channel.ConfirmSelect();
            var properties = channel.CreateBasicProperties(); properties.Persistent = true; properties.ContentType = "application/json"; properties.MessageId = pending.Id.ToString();
            if (activity?.Id is { } traceParent) properties.Headers = new Dictionary<string, object> { ["traceparent"] = traceParent };
            var returned = false;
            channel.BasicReturn += (_, _) => returned = true;
            channel.BasicPublish("inklink.scheduling.events.v1", "scheduling.reservation-confirmed.v1", mandatory: true, properties, Encoding.UTF8.GetBytes(pending.Payload));
            channel.WaitForConfirmsOrDie(TimeSpan.FromSeconds(5));
            if (returned) throw new InvalidOperationException("Broker returned an unroutable event.");
            activity?.SetStatus(ActivityStatusCode.Ok);
            pending.PublishedAt = DateTimeOffset.UtcNow; pending.LastError = null; pending.NextAttemptAt = null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            pending.RecordFailure(exception.GetType().Name, DateTimeOffset.UtcNow);
            logger.LogWarning("Outbox publish failed for {EventId}: {FailureType}; attempts {Attempts}; quarantined {Quarantined}", pending.Id, pending.LastError, pending.Attempts, pending.QuarantinedAt.HasValue);
        }
        finally { activity?.Dispose(); }
        await database.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        EventTelemetry.Record(pending.PublishedAt.HasValue ? "published" : pending.QuarantinedAt.HasValue ? "quarantined" : "retry", started, pending.CreatedAt);
    }
}
