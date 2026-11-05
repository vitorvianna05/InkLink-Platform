using InkLink.Identity.Domain;

namespace InkLink.Identity.Application;

public interface ICurrentUser { Guid? UserId { get; } }
public interface IProfileReader
{
    Task<Profile?> FindByIdAsync(Guid id, CancellationToken cancellationToken);
}
/// <summary>Perfil com a foto (512 px) e a miniatura (128 px); endereços assinados para foto privada.</summary>
public sealed record ProfileResponse(Guid Id, string DisplayName, string? PhotoUrl = null, string? PhotoThumbnailUrl = null);
public enum ProfileStatus { Found, Unauthenticated, NotFound }
public sealed record ProfileResult(ProfileStatus Status, ProfileResponse? Profile = null);

public sealed class GetMyProfile(ICurrentUser currentUser, IProfileReader profiles, IProfilePhotos photos)
{
    public async Task<ProfileResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not { } id || id == Guid.Empty)
            return new(ProfileStatus.Unauthenticated);
        var profile = await profiles.FindByIdAsync(id, cancellationToken);
        if (profile is null) return new(ProfileStatus.NotFound);
        var photo = await photos.ResolveAsync(profile.PhotoKey, profile.PhotoPublic, cancellationToken);
        return new(ProfileStatus.Found, new(profile.Id, profile.DisplayName, photo?.Url, photo?.ThumbnailUrl));
    }
}
