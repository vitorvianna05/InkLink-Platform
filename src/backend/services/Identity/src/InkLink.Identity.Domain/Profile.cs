namespace InkLink.Identity.Domain;

public sealed class Profile
{
    private Profile() { }
    public Guid Id { get; private set; }
    public bool IsArtist { get; private set; }
    public void ActivateArtist() => IsArtist = true;
    public void Rename(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        DisplayName = displayName.Trim();
    }
    public string DisplayName { get; private set; } = string.Empty;

    public static Profile Create(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        return new Profile { Id = Guid.CreateVersion7(), DisplayName = displayName.Trim() };
    }
}
