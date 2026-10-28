using InkLink.Identity.Domain;

namespace InkLink.Identity.Application;

// Explicit bootstrap command, separate from the read-only GetMyProfile query.
public sealed class ProvisionAccount(IAccountStore accounts)
{
    public async Task<AccountIdentity> ExecuteAsync(string issuer, string subject, string displayName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        var existing = await accounts.FindByExternalIdentityAsync(issuer, subject, cancellationToken);
        if (existing is not null) return new(existing.Id, existing.IsArtist);
        var profile = Profile.Create(displayName);
        var link = ExternalAccount.Link(issuer, subject, profile.Id);
        if (await accounts.TryAddAsync(profile, link, cancellationToken)) return new(profile.Id, profile.IsArtist);
        var winner = await accounts.FindByExternalIdentityAsync(issuer, subject, cancellationToken)
            ?? throw new InvalidOperationException("Unable to resolve the concurrently provisioned account.");
        return new(winner.Id, winner.IsArtist);
    }
}
