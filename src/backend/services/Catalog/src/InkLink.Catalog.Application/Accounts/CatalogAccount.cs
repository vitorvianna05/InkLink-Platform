namespace InkLink.Catalog.Application.Accounts;

public sealed record CatalogAccount(Guid Id, bool IsArtist);
public interface ICurrentCatalogAccount { CatalogAccount? Account { get; } }
public interface IIdentityDirectory { Task<CatalogAccount> ResolveAsync(string credential, CancellationToken cancellationToken); }

public sealed class ResolveCatalogAccount(IIdentityDirectory directory)
{
    public Task<CatalogAccount> ExecuteAsync(string credential, CancellationToken cancellationToken) => directory.ResolveAsync(credential, cancellationToken);
}

internal static class AccountAccess
{
    public static CatalogAccount Require(ICurrentCatalogAccount current, bool artist = false)
    {
        var account = current.Account ?? throw new CatalogFailure(CatalogError.Unauthenticated, "authentication.required");
        if (artist && !account.IsArtist) throw new CatalogFailure(CatalogError.Forbidden, "catalog.artist_required");
        return account;
    }
}
