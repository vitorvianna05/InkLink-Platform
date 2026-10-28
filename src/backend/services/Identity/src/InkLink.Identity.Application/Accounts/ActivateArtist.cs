namespace InkLink.Identity.Application;

// Called only by the administrative executable; it is not mapped to a public endpoint.
public sealed class ActivateArtist(IAccountStore accounts)
{
    public async Task<bool> ExecuteAsync(string issuer, string subject, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        var profile = await accounts.FindByExternalIdentityAsync(issuer, subject, cancellationToken);
        if (profile is null) return false;
        profile.ActivateArtist();
        await accounts.SaveChangesAsync(cancellationToken);
        return true;
    }
}
