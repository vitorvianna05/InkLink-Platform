namespace InkLink.Catalog.Application.Queries;

// Private, scoped service contract. Owner identity is never added to public catalog DTOs.
public sealed record EngagementFlash(Guid Id, string Title, string ImageUrl, string AlternativeText, string Style, decimal? ReferencePrice);
public sealed record EngagementContext(Guid ArtistId, Guid ArtistAccountId, string Name, string Studio, string City, decimal StartingPrice, EngagementFlash? Flash);
public interface IEngagementContextReader
{
    Task<EngagementContext> ReadAsync(Guid artistId, Guid? flashId, CancellationToken cancellationToken);
}
