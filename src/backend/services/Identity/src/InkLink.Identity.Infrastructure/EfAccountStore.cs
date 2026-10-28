using InkLink.Identity.Application;
using InkLink.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace InkLink.Identity.Infrastructure;

internal sealed class EfAccountStore(IdentityDbContext database) : IAccountStore
{
    public Task<Profile?> FindByExternalIdentityAsync(string issuer, string subject, CancellationToken cancellationToken) =>
        (from account in database.ExternalAccounts
         join profile in database.Profiles on account.ProfileId equals profile.Id
         where account.Issuer == issuer && account.Subject == subject
         select profile).SingleOrDefaultAsync(cancellationToken);

    public Task<Profile?> FindByIdAsync(Guid profileId, CancellationToken cancellationToken) =>
        database.Profiles.SingleOrDefaultAsync(profile => profile.Id == profileId, cancellationToken);

    public async Task<bool> TryAddAsync(Profile profile, ExternalAccount link, CancellationToken cancellationToken)
    {
        database.Profiles.Add(profile);
        database.ExternalAccounts.Add(link);
        try
        {
            // EF commits the profile and its link atomically in this SaveChanges call.
            await database.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "PK_external_accounts" })
        {
            database.ChangeTracker.Clear();
            return false;
        }
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => database.SaveChangesAsync(cancellationToken);
}
