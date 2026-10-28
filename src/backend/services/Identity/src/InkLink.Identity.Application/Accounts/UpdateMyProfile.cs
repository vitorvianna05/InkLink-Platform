namespace InkLink.Identity.Application;

public enum UpdateProfileStatus { Updated, Unauthenticated, NotFound, InvalidName }

public sealed class UpdateMyProfile(ICurrentUser currentUser, IAccountStore accounts)
{
    public async Task<UpdateProfileStatus> ExecuteAsync(string? displayName, CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not { } id || id == Guid.Empty) return UpdateProfileStatus.Unauthenticated;
        if (string.IsNullOrWhiteSpace(displayName)) return UpdateProfileStatus.InvalidName;
        var profile = await accounts.FindByIdAsync(id, cancellationToken);
        if (profile is null) return UpdateProfileStatus.NotFound;
        profile.Rename(displayName);
        await accounts.SaveChangesAsync(cancellationToken);
        return UpdateProfileStatus.Updated;
    }
}
