using InkLink.Identity.Domain;

namespace InkLink.Identity.Application;

public sealed record AccountIdentity(Guid ProfileId, bool IsArtist);

public interface IAccountStore
{
    Task<Profile?> FindByExternalIdentityAsync(string issuer, string subject, CancellationToken cancellationToken);
    Task<Profile?> FindByIdAsync(Guid profileId, CancellationToken cancellationToken);
    Task<bool> TryAddAsync(Profile profile, ExternalAccount link, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
