namespace InkLink.Scheduling.Infrastructure.Persistence;

public sealed class FrozenEngagement
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public Guid ArtistAccountId { get; set; }
}
