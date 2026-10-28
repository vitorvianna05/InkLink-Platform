using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace InkLink.Bff;

public sealed class PostgresTicketStore(IDbContextFactory<BffDbContext> databases, IDataProtectionProvider protection) : ITicketStore
{
    private readonly IDataProtector protector = protection.CreateProtector("InkLink.Bff.Ticket.v1");
    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await using var database = await databases.CreateDbContextAsync();
        await database.Sessions.Where(session => session.ExpiresAt < DateTimeOffset.UtcNow).ExecuteDeleteAsync();
        database.Sessions.Add(new() { Id = key, ProtectedTicket = Protect(ticket), ExpiresAt = ticket.Properties.ExpiresUtc!.Value });
        await database.SaveChangesAsync();
        return key;
    }
    public async Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        await using var database = await databases.CreateDbContextAsync();
        var data = Protect(ticket);
        var expires = ticket.Properties.ExpiresUtc!.Value;
        await database.Sessions.Where(session => session.Id == key)
            .ExecuteUpdateAsync(setters => setters.SetProperty(session => session.ProtectedTicket, data).SetProperty(session => session.ExpiresAt, expires));
    }
    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        await using var database = await databases.CreateDbContextAsync();
        var session = await database.Sessions.AsNoTracking().SingleOrDefaultAsync(session => session.Id == key);
        if (session is null || session.ExpiresAt <= DateTimeOffset.UtcNow) return null;
        try { return TicketSerializer.Default.Deserialize(protector.Unprotect(session.ProtectedTicket)); }
        catch (CryptographicException) { return null; }
    }
    public async Task RemoveAsync(string key)
    {
        await using var database = await databases.CreateDbContextAsync();
        await database.Sessions.Where(session => session.Id == key).ExecuteDeleteAsync();
    }
    private byte[] Protect(AuthenticationTicket ticket) => protector.Protect(TicketSerializer.Default.Serialize(ticket));
}
