using InkLink.Identity.Domain;

namespace InkLink.Identity.Application;

public interface ICurrentUser { Guid? UserId { get; } }
public interface IProfileReader
{
    Task<Profile?> FindByIdAsync(Guid id, CancellationToken cancellationToken);
}
public sealed record ProfileResponse(Guid Id, string DisplayName);
public enum ProfileStatus { Found, Unauthenticated, NotFound }
public sealed record ProfileResult(ProfileStatus Status, ProfileResponse? Profile = null);

public sealed class GetMyProfile(ICurrentUser currentUser, IProfileReader profiles)
{
    public async Task<ProfileResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not { } id || id == Guid.Empty)
            return new(ProfileStatus.Unauthenticated);
        var profile = await profiles.FindByIdAsync(id, cancellationToken);
        return profile is null ? new(ProfileStatus.NotFound)
            : new(ProfileStatus.Found, new(profile.Id, profile.DisplayName));
    }
}
