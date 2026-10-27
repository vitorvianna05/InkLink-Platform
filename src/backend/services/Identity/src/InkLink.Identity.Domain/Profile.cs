namespace InkLink.Identity.Domain;

public sealed class Profile
{
    private Profile() { }
    public Guid Id { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;

    public static Profile Create(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        return new Profile { Id = Guid.CreateVersion7(), DisplayName = displayName.Trim() };
    }
}
