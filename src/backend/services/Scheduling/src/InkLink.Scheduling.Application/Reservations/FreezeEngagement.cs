namespace InkLink.Scheduling.Application.Reservations;

public sealed record FreezeInput(Guid ClientId, Guid ArtistAccountId);
public interface IEngagementFreezeStore
{
    Task FreezeAsync(Guid engagement, FreezeInput input, DateTimeOffset now, Action<bool, bool> validate, CancellationToken ct);
}
public sealed class FreezeEngagement(IEngagementFreezeStore store, TimeProvider clock)
{
    public Task ExecuteAsync(Guid engagement, FreezeInput input, CancellationToken ct)
    {
        if (engagement == Guid.Empty || input.ClientId == Guid.Empty || input.ArtistAccountId == Guid.Empty || input.ClientId == input.ArtistAccountId)
            throw SchedulingFailure.Invalid("participants", "Nonempty distinct participants required.");
        return store.FreezeAsync(engagement, input, clock.GetUtcNow(), (future, mismatch) =>
        {
            try { InkLink.Scheduling.Domain.ReservationClosure.EnsureAllowed(future, mismatch); }
            catch (InkLink.Scheduling.Domain.ClosureConflict ex) { throw new SchedulingFailure(SchedulingError.Conflict, ex.Message); }
        }, ct);
    }
}
