namespace InkLink.Finance.Application.Accounts;

public sealed record FinanceAccount(Guid Id, bool IsArtist);
public interface ICurrentFinanceAccount { FinanceAccount? Account { get; } string Credential { get; } }
public interface IIdentityDirectory { Task<FinanceAccount> ResolveAsync(string credential, CancellationToken cancellationToken); }

public sealed class ResolveFinanceAccount(IIdentityDirectory directory)
{
    public Task<FinanceAccount> ExecuteAsync(string credential, CancellationToken cancellationToken) => directory.ResolveAsync(credential, cancellationToken);
}

internal static class AccountAccess
{
    public static FinanceAccount Require(ICurrentFinanceAccount current, bool artist = false)
    {
        var account = current.Account ?? throw new FinanceFailure(FinanceError.Unauthenticated, "authentication.required");
        if (artist && !account.IsArtist) throw new FinanceFailure(FinanceError.Forbidden, "finance.artist_required");
        return account;
    }
}
