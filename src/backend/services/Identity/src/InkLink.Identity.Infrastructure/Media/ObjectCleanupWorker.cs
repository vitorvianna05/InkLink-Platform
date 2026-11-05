using InkLink.ObjectStorage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InkLink.Identity.Infrastructure.Media;

/// <summary>Remove fotos obsoletas ou órfãs (decisão 0018); SKIP LOCKED permite várias instâncias.</summary>
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
        var database = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var now = time.GetUtcNow();
        var due = await database.ObjectCleanups
            .FromSql($"SELECT * FROM identity.object_cleanups WHERE \"DueAt\" <= {now} ORDER BY \"DueAt\" LIMIT 50 FOR UPDATE SKIP LOCKED")
            .ToListAsync(cancellationToken);
        var removed = 0;
        foreach (var item in due)
        {
            try
            {
                await storage.DeleteAsync(item.Private ? ObjectVisibility.Private : ObjectVisibility.Public, item.Key, cancellationToken);
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
        return removed;
    }
}
