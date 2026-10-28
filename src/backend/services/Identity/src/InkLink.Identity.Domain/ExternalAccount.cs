namespace InkLink.Identity.Domain;

public sealed class ExternalAccount
{
    private ExternalAccount() { }
    public string Issuer { get; private set; } = string.Empty;
    public string Subject { get; private set; } = string.Empty;
    public Guid ProfileId { get; private set; }
    public static ExternalAccount Link(string issuer, string subject, Guid profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        if (profileId == Guid.Empty) throw new ArgumentException("A profile identifier is required.", nameof(profileId));
        return new() { Issuer = issuer, Subject = subject, ProfileId = profileId };
    }
}
