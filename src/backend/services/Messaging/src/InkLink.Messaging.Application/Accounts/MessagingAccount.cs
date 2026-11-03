namespace InkLink.Messaging.Application.Accounts;

public sealed record MessagingAccount(Guid Id, bool IsArtist);
public interface ICurrentMessagingAccount { MessagingAccount? Account { get; } }
public interface IIdentityDirectory { Task<MessagingAccount> ResolveAsync(string credential, CancellationToken cancellationToken); }

public sealed class ResolveMessagingAccount(IIdentityDirectory directory)
{
    public Task<MessagingAccount> ExecuteAsync(string credential, CancellationToken cancellationToken) => directory.ResolveAsync(credential, cancellationToken);
}

internal static class AccountAccess
{
    public static MessagingAccount Require(ICurrentMessagingAccount current, bool artist = false)
    {
        var account = current.Account ?? throw new MessagingFailure(MessagingError.Unauthenticated, "authentication.required");
        if (artist && !account.IsArtist) throw new MessagingFailure(MessagingError.Forbidden, "messaging.artist_required");
        return account;
    }
}
