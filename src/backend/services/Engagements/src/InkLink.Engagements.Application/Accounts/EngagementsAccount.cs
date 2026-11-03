namespace InkLink.Engagements.Application.Accounts;

public sealed record EngagementsAccount(Guid Id, bool IsArtist);
public interface ICurrentEngagementsAccount { EngagementsAccount? Account { get; } }
public interface IIdentityDirectory { Task<EngagementsAccount> ResolveAsync(string credential, CancellationToken cancellationToken); }

public sealed class ResolveEngagementsAccount(IIdentityDirectory directory)
{
    public Task<EngagementsAccount> ExecuteAsync(string credential, CancellationToken cancellationToken) => directory.ResolveAsync(credential, cancellationToken);
}

internal static class AccountAccess
{
    public static EngagementsAccount Require(ICurrentEngagementsAccount current, bool artist = false)
    {
        var account = current.Account ?? throw new EngagementsFailure(EngagementsError.Unauthenticated, "authentication.required");
        if (artist && !account.IsArtist) throw new EngagementsFailure(EngagementsError.Forbidden, "engagements.artist_required");
        return account;
    }
}
