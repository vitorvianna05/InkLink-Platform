using InkLink.Catalog.Infrastructure.Persistence;
using InkLink.ObjectStorage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InkLink.Catalog.Infrastructure.Media;

/// <summary>
/// Remove objetos obsoletos ou órfãos registrados em object_cleanups (decisão 0018). Linhas vencidas são
/// travadas com SKIP LOCKED, o que permite várias instâncias; falhas recebem nova tentativa com espera crescente.
/// </summary>
public sealed class ObjectCleanupWorker(IServiceScopeFactory scopes, TimeProvider time, ILogger<ObjectCleanupWorker> logger) : BackgroundService
{
    public static TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Task.Delay(Interval, time, stoppingToken); }
            catch (OperationCanceledException) { return; }
            try { await RunOnceAsync(stoppingToken); }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning("Object cleanup pass failed; it will be retried.");
            }
        }
    }

    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var now = time.GetUtcNow();
        var due = await database.ObjectCleanups
            .FromSql($"SELECT * FROM catalog.object_cleanups WHERE \"DueAt\" <= {now} ORDER BY \"DueAt\" LIMIT 50 FOR UPDATE SKIP LOCKED")
            .ToListAsync(cancellationToken);
        var removed = 0;
        foreach (var item in due)
        {
            try
            {
                await storage.DeleteAsync(ObjectVisibility.Public, item.Key, cancellationToken);
                database.ObjectCleanups.Remove(item);
                removed++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                item.Attempts++;
                item.DueAt = now.Add(TimeSpan.FromMinutes(Math.Min(60, Math.Pow(2, Math.Min(item.Attempts, 6)))));
            }
        }
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        if (due.Count > 0) logger.LogInformation("Object cleanup removed {Removed} of {Due} due objects.", removed, due.Count);
        return removed;
    }
}
