namespace InkLink.Scheduling.Application.Accounts;

public sealed record SchedulingAccount(Guid Id, bool IsArtist);
public interface ICurrentSchedulingAccount { SchedulingAccount? Account { get; } }
public interface IIdentityDirectory { Task<SchedulingAccount> ResolveAsync(string credential, CancellationToken cancellationToken); }

public sealed class ResolveSchedulingAccount(IIdentityDirectory directory)
{
    public Task<SchedulingAccount> ExecuteAsync(string credential, CancellationToken cancellationToken) => directory.ResolveAsync(credential, cancellationToken);
}

internal static class AccountAccess
{
    public static SchedulingAccount Require(ICurrentSchedulingAccount current, bool artist = false)
    {
        var account = current.Account ?? throw new SchedulingFailure(SchedulingError.Unauthenticated, "authentication.required");
        if (artist && !account.IsArtist) throw new SchedulingFailure(SchedulingError.Forbidden, "scheduling.artist_required");
        return account;
    }
}
