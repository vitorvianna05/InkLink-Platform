using System.Diagnostics;
using System.Text;
using InkLink.Engagements.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace InkLink.Engagements.Infrastructure.Messaging;

public sealed class OutboxWorker(IServiceScopeFactory scopes, IConfiguration configuration, IHostEnvironment environment, ILogger<OutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var database = scope.ServiceProvider.GetRequiredService<EngagementsDbContext>();
                await BackfillAsync(database, stoppingToken);
                await PublishOneAsync(database, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogWarning("Outbox cycle failed: {FailureType}", exception.GetType().Name); }
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }

    private static async Task BackfillAsync(EngagementsDbContext database, CancellationToken ct)
    {
        var missing = await database.Engagements.AsNoTracking().Where(e => !database.Outbox.Any(o => o.EngagementId == e.Id && o.Type == "engagement.negotiation-opened.v1"))
            .OrderBy(e => e.Id).Take(100).ToListAsync(ct);
        if (missing.Count == 0) return;
        database.Outbox.AddRange(missing.Select(OutboxEvent.ForNegotiation));
        try { await database.SaveChangesAsync(ct); }
        catch (DbUpdateException) { database.ChangeTracker.Clear(); throw; }
    }

    private async Task PublishOneAsync(EngagementsDbContext database, CancellationToken ct)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(ct);
        var pending = await new OutboxQueue(database).LockNextAsync(ct);
        if (pending is null) { await transaction.CommitAsync(ct); return; }
        var started = Stopwatch.GetTimestamp();
        using var activity = EventTelemetry.Start(pending.Payload);
        activity?.SetTag("messaging.message.id", pending.Id.ToString());
        try
        {
            var uri = configuration["Broker:Uri"] ?? throw new InvalidOperationException("Broker URI missing.");
            var broker = new Uri(uri);
            if (broker.Scheme != "amqps" && !(broker.Scheme == "amqp" && (environment.IsDevelopment() || environment.IsEnvironment("Testing"))))
                throw new InvalidOperationException("Broker requires TLS outside local/test environments.");
            var factory = new ConnectionFactory { Uri = broker, RequestedConnectionTimeout = TimeSpan.FromSeconds(5), AutomaticRecoveryEnabled = false };
            using var connection = factory.CreateConnection("inklink-engagements-outbox");
            using var channel = connection.CreateModel();
            channel.ConfirmSelect();
            var properties = channel.CreateBasicProperties(); properties.Persistent = true; properties.ContentType = "application/json"; properties.MessageId = pending.Id.ToString();
            if (activity?.Id is { } traceParent) properties.Headers = new Dictionary<string, object> { ["traceparent"] = traceParent };
            var returned = false;
            channel.BasicReturn += (_, _) => returned = true;
            channel.BasicPublish("inklink.engagements.events.v1", pending.Type, mandatory: true, properties, Encoding.UTF8.GetBytes(pending.Payload));
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
        await database.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        EventTelemetry.Record(pending.PublishedAt.HasValue ? "published" : pending.QuarantinedAt.HasValue ? "quarantined" : "retry", started, pending.CreatedAt);
    }
}
