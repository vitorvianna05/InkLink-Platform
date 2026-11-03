using InkLink.Engagements.Application.Accounts;
using InkLink.Engagements.Domain;
namespace InkLink.Engagements.Application.Lifecycle;

public sealed record LifecycleView(Guid Id, Guid EngagementId, string Kind, string Status, int Attempts, string? Error, DateTimeOffset UpdatedAt);
public sealed record LifecycleStatus(bool RequiresReservation, string State, IReadOnlyList<LifecycleView> Operations);
public sealed record PendingLifecycle(Guid Id, string? TraceParent, string? CorrelationId);
public sealed record LifecycleChange<T>(T Result, LifecycleOperation? NewOperation = null);
public interface ILifecycleStore
{
    Task<T> ChangeAsync<T>(Guid id, Guid participant, Func<Engagement, IReadOnlyList<LifecycleOperation>, LifecycleChange<T>> change, CancellationToken ct);
    Task<LifecycleStatus?> ReadAsync(Guid id, Guid participant, CancellationToken ct);
    Task<IReadOnlyList<PendingLifecycle>> PendingAsync(CancellationToken ct);
    Task RunAsync(Guid id, Func<Engagement, LifecycleOperation, CancellationToken, Task> run, CancellationToken ct);
}
public interface ILifecycleDirectory
{
    Task<bool> FreezeAsync(Engagement e, CancellationToken ct);
    Task ApplyConversationAsync(Engagement e, bool close, CancellationToken ct);
}
public sealed class ManageLifecycle(ICurrentEngagementsAccount current, ILifecycleStore store)
{
    public Task<LifecycleView> CloseAsync(Guid id, bool confirmed, CancellationToken ct)
    {
        var actor = AccountAccess.Require(current);
        if (!confirmed) throw EngagementsFailure.Invalid("confirmed", "Explicit confirmation is required.");
        var correlation = LifecycleCorrelation.Capture();
        return store.ChangeAsync(id, actor.Id, (engagement, operations) =>
        {
            var op = operations.SingleOrDefault(x => x.Kind == "Close");
            var created = op is null;
            if (op is null) op = LifecycleOperation.Create(engagement, "Close", correlation.TraceParent, correlation.CorrelationId);
            else if (op.Status == "Rejected") op.Retry(engagement);
            engagement.BeginClosing();
            return new LifecycleChange<LifecycleView>(View(op), created ? op : null);
        }, ct);
    }
    public async Task<LifecycleStatus> ReadAsync(Guid id, CancellationToken ct) =>
        await store.ReadAsync(id, AccountAccess.Require(current).Id, ct) ?? throw new EngagementsFailure(EngagementsError.NotFound, "engagements.not_found");
    public async Task RetryAsync(Guid id, CancellationToken ct) =>
        await store.ChangeAsync(id, AccountAccess.Require(current).Id, (engagement, operations) =>
        {
            foreach (var op in operations.Where(x => x.Status == "Quarantined")) op.Retry(engagement);
            return new LifecycleChange<bool>(true);
        }, ct);
    public static LifecycleView View(LifecycleOperation op) => new(op.Id, op.EngagementId, op.Kind, op.Status, op.Attempts, op.Error, op.UpdatedAt);
}
public sealed class CoordinateLifecycle(ILifecycleStore store, ILifecycleDirectory directory)
{
    public Task RunAsync(Guid id, CancellationToken ct) => store.RunAsync(id, async (engagement, operation, token) =>
    {
        if (operation.Status != "Pending" || operation.NextAttemptAt > DateTimeOffset.UtcNow) return;
        try
        {
            if (operation.Kind == "Close")
            {
                if (!await directory.FreezeAsync(engagement, token))
                {
                    engagement.RejectClosing(operation.PreviousState);
                    operation.Reject("engagements.future_reservations"); return;
                }
                await directory.ApplyConversationAsync(engagement, true, token);
                engagement.CompleteClosing();
            }
            else
            {
                if (engagement.State == EngagementState.Closed)
                { operation.Reject("engagements.closing"); return; }
                // Closing is provisional: it can be rejected by Scheduling. Keep activation recoverable.
                if (engagement.State == EngagementState.Closing)
                { operation.Defer("engagements.awaiting_closure"); return; }
                await directory.ApplyConversationAsync(engagement, false, token);
            }
            operation.Complete();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception) { operation.Fail("engagements.lifecycle_dependency_unavailable"); }
    }, ct);
}
