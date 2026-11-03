namespace InkLink.Engagements.Application.Scheduling;

public sealed record SchedulingContext(Guid Id, Guid ClientId, Guid ArtistAccountId, string State, string Project, string ArtistName,
    Guid? ProposalId, string? ProposalState, decimal? Amount, long Revision);
public interface ISchedulingContextStore { Task<SchedulingContext?> ReadAsync(Guid id, CancellationToken ct); }
public sealed class ReadSchedulingContext(ISchedulingContextStore store)
{
    public async Task<SchedulingContext> ExecuteAsync(Guid id, CancellationToken ct) =>
        await store.ReadAsync(id, ct) ?? throw new EngagementsFailure(EngagementsError.NotFound, "engagements.not_found");
}
