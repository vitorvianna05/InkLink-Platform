using InkLink.Identity.Domain;

namespace InkLink.Identity.Application;

// Explicit bootstrap command, separate from the read-only GetMyProfile query.
public sealed class ProvisionAccount(IAccountStore accounts)
{
    /// <param name="selfRegistered">Conta criada pela página de cadastro (email_verified falso): nasce pendente (decisão 0020).</param>
    public async Task<AccountIdentity> ExecuteAsync(string issuer, string subject, string displayName, CancellationToken cancellationToken = default, bool selfRegistered = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        var existing = await accounts.FindByExternalIdentityAsync(issuer, subject, cancellationToken);
        if (existing is not null) return Identity(existing);
        var profile = selfRegistered ? Profile.CreatePending(displayName) : Profile.Create(displayName);
        var link = ExternalAccount.Link(issuer, subject, profile.Id);
        if (await accounts.TryAddAsync(profile, link, cancellationToken)) return Identity(profile);
        var winner = await accounts.FindByExternalIdentityAsync(issuer, subject, cancellationToken)
            ?? throw new InvalidOperationException("Unable to resolve the concurrently provisioned account.");
        return Identity(winner);
    }

    private static AccountIdentity Identity(Profile profile) => new(profile.Id, profile.IsArtist, profile.RegistrationPending);
}
