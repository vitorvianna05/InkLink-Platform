using InkLink.Engagements.Domain;

namespace InkLink.Engagements.Application.Negotiations;

public sealed record RequestInput(Guid ArtistId, string Idea, string BodyRegion, decimal ApproximateSizeCm, Guid? FlashId = null);
public sealed record ProposalInput(string Description, decimal Amount);
public sealed record FlashSnapshot(Guid Id, string Title, string ImageUrl, string AlternativeText, string Style, decimal? ReferencePrice);
public sealed record CatalogContext(Guid ArtistId, Guid ArtistAccountId, string Name, string Studio, string City, decimal StartingPrice, FlashSnapshot? Flash);
public sealed record CatalogSnapshot(Guid ArtistId, string Name, string Studio, string City, decimal StartingPrice, FlashSnapshot? Flash);
public sealed record ProposalView(Guid Id, string Description, decimal Amount, string Currency, string State, DateTimeOffset CreatedAt, DateTimeOffset? AcceptedAt);
public sealed record EngagementView(Guid Id, Guid ClientId, Guid ArtistAccountId, string Idea, string BodyRegion, decimal ApproximateSizeCm,
    CatalogSnapshot Catalog, string State, DateTimeOffset CreatedAt, long Revision, Guid? CurrentProposalId, ProposalView? CurrentProposal);
public sealed record Page<T>(IReadOnlyList<T> Items, string? NextCursor, bool HasMore, int Total);
public sealed record Creation<T>(T Value, bool Created);
public sealed record EngagementChange<T>(T Result, Proposal? NewProposal = null);

public interface ICatalogDirectory
{
    Task<CatalogContext> GetAsync(Guid artistId, Guid? flashId, CancellationToken cancellationToken);
}

public interface IEngagementStore
{
    Task<Engagement?> FindRequestAsync(Guid clientId, Guid key, CancellationToken cancellationToken);
    Task<Creation<Engagement>> CreateAsync(Engagement engagement, CancellationToken cancellationToken);
    Task<EngagementView?> ReadAsync(Guid id, Guid participant, CancellationToken cancellationToken);
    Task<Page<EngagementView>> ListAsync(Guid participant, string perspective, int limit, Guid? after, CancellationToken cancellationToken);
    Task<ProposalView?> ReadProposalAsync(Guid id, Guid proposalId, Guid participant, CancellationToken cancellationToken);
    Task<Page<ProposalView>> ListProposalsAsync(Guid id, Guid participant, int limit, Guid? after, CancellationToken cancellationToken);
    Task<T> ChangeAsync<T>(Guid id, Guid participant, bool asArtist, Guid? requestKey, Guid? proposalId,
        Func<Engagement, IReadOnlyList<Proposal>, EngagementChange<T>> change, CancellationToken cancellationToken);
}
