using System.Data;
using System.Text.Json;
using InkLink.Engagements.Application;
using InkLink.Engagements.Application.Negotiations;
using InkLink.Engagements.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace InkLink.Engagements.Infrastructure.Persistence;

public sealed class EfEngagementStore(EngagementsDbContext database) : IEngagementStore
{
    public Task<Engagement?> FindRequestAsync(Guid clientId, Guid key, CancellationToken cancellationToken) =>
        database.Engagements.AsNoTracking().SingleOrDefaultAsync(e => e.ClientId == clientId && e.RequestKey == key, cancellationToken);

    public async Task<Creation<Engagement>> CreateAsync(Engagement engagement, CancellationToken cancellationToken)
    {
        database.Engagements.Add(engagement);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return new(engagement, true);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            database.ChangeTracker.Clear();
            var previous = await FindRequestAsync(engagement.ClientId, engagement.RequestKey, cancellationToken);
            if (previous is null) throw;
            return new(previous, false);
        }
    }

    public async Task<EngagementView?> ReadAsync(Guid id, Guid participant, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var engagement = await database.Engagements.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id && (e.ClientId == participant || e.ArtistAccountId == participant), cancellationToken);
        if (engagement is null) return null;
        var proposal = await database.Proposals.AsNoTracking().SingleOrDefaultAsync(p => p.EngagementId == id && p.Id == engagement.CurrentProposalId, cancellationToken);
        return View(engagement, proposal);
    }

    public async Task<Page<EngagementView>> ListAsync(Guid participant, string perspective, int limit, Guid? after, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var query = database.Engagements.AsNoTracking().Where(e => perspective == "artist" ? e.ArtistAccountId == participant : e.ClientId == participant);
        var total = await query.CountAsync(cancellationToken);
        if (after.HasValue) query = query.Where(e => e.Id.CompareTo(after.Value) > 0);
        var items = await query.OrderBy(e => e.Id).Take(limit + 1).ToListAsync(cancellationToken);
        var more = items.Count > limit;
        items = items.Take(limit).ToList();
        var ids = items.Where(e => e.CurrentProposalId.HasValue).Select(e => e.CurrentProposalId!.Value).ToArray();
        var proposals = await database.Proposals.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);
        return new(items.Select(e => View(e, e.CurrentProposalId.HasValue ? proposals.GetValueOrDefault(e.CurrentProposalId.Value) : null)).ToList(), null, more, total);
    }

    public async Task<ProposalView?> ReadProposalAsync(Guid id, Guid proposalId, Guid participant, CancellationToken cancellationToken)
    {
        var proposal = await database.Proposals.AsNoTracking().Where(p => p.Id == proposalId && p.EngagementId == id
            && database.Engagements.Any(e => e.Id == id && (e.ClientId == participant || e.ArtistAccountId == participant))).SingleOrDefaultAsync(cancellationToken);
        return proposal is null ? null : View(proposal);
    }

    public async Task<Page<ProposalView>> ListProposalsAsync(Guid id, Guid participant, int limit, Guid? after, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        if (!await database.Engagements.AnyAsync(e => e.Id == id && (e.ClientId == participant || e.ArtistAccountId == participant), cancellationToken))
            throw ManageEngagements.NotFound();
        var query = database.Proposals.AsNoTracking().Where(p => p.EngagementId == id);
        var total = await query.CountAsync(cancellationToken);
        if (after.HasValue) query = query.Where(p => p.Id.CompareTo(after.Value) > 0);
        var items = await query.OrderBy(p => p.Id).Take(limit + 1).ToListAsync(cancellationToken);
        return new(items.Take(limit).Select(View).ToArray(), null, items.Count > limit, total);
    }

    public async Task<T> ChangeAsync<T>(Guid id, Guid participant, bool asArtist, Guid? requestKey, Guid? proposalId,
        Func<Engagement, IReadOnlyList<Proposal>, EngagementChange<T>> change, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var engagement = await LockAsync(id, participant, asArtist, cancellationToken);
        var query = database.Proposals.Where(p => p.EngagementId == id && (p.Id == engagement.CurrentProposalId
            || (requestKey.HasValue && p.RequestKey == requestKey) || (proposalId.HasValue && p.Id == proposalId)));
        var proposals = await query.ToListAsync(cancellationToken);
        var mutation = change(engagement, proposals);
        if (mutation.NewProposal is not null) database.Proposals.Add(mutation.NewProposal);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return mutation.Result;
    }

    private async Task<Engagement> LockAsync(Guid id, Guid account, bool artist, CancellationToken cancellationToken)
    {
        // Restrict access in the locking query; third-party IDs never expose participant data.
        var query = artist
            ? database.Engagements.FromSqlInterpolated($"SELECT * FROM engagements.engagements WHERE \"Id\" = {id} AND \"ArtistAccountId\" = {account} FOR UPDATE")
            : database.Engagements.FromSqlInterpolated($"SELECT * FROM engagements.engagements WHERE \"Id\" = {id} AND \"ClientId\" = {account} FOR UPDATE");
        return await query.SingleOrDefaultAsync(cancellationToken) ?? throw ManageEngagements.NotFound();
    }

    private static EngagementView View(Engagement e, Proposal? proposal) => new(e.Id, e.ClientId, e.ArtistAccountId, e.Idea, e.BodyRegion, e.ApproximateSizeCm,
        JsonSerializer.Deserialize<CatalogSnapshot>(e.CatalogSnapshotJson)!, e.State.ToString(), e.CreatedAt, e.Revision, e.CurrentProposalId, proposal is null ? null : View(proposal));
    private static ProposalView View(Proposal p) => new(p.Id, p.Description, p.Amount, "BRL", p.State.ToString(), p.CreatedAt, p.AcceptedAt);
}
