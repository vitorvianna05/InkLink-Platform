using System.Diagnostics;
using System.Diagnostics.Metrics;
using InkLink.Engagements.Application.Lifecycle;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
namespace InkLink.Engagements.Infrastructure;

public sealed class LifecycleWorker(IServiceScopeFactory scopes, ILogger<LifecycleWorker> logger) : BackgroundService
{
    private static readonly ActivitySource Activities = new("InkLink.Engagements.Lifecycle");
    private static readonly Meter Metrics = new("InkLink.Engagements.Lifecycle");
    private static readonly Counter<long> Cycles = Metrics.CreateCounter<long>("inklink.lifecycle.cycles");
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var read = scopes.CreateScope();
                var ids = await read.ServiceProvider.GetRequiredService<ILifecycleStore>().PendingAsync(stoppingToken);
                foreach (var pending in ids)
                {
                    using var scope = scopes.CreateScope();
                    ActivityContext.TryParse(pending.TraceParent, null, isRemote: true, out var parent);
                    using var activity = Activities.StartActivity("lifecycle.coordinate", ActivityKind.Internal, parent);
                    var id = pending.Id;
                    activity?.SetTag("inklink.operation.id", id.ToString());
                    using var log = logger.BeginScope(new Dictionary<string, object?> { ["operationId"] = id, ["correlationId"] = pending.CorrelationId, ["traceId"] = activity?.TraceId.ToString() });
                    await scope.ServiceProvider.GetRequiredService<CoordinateLifecycle>().RunAsync(id, stoppingToken);
                    Cycles.Add(1);
                    logger.LogInformation("Lifecycle operation {OperationId} checked; durable status is available to participants", id);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { logger.LogWarning("Lifecycle processing unavailable; pending operations remain durable."); }
            try { await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
