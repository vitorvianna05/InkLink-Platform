using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace InkLink.Bff.Extensions;

internal static class BffPersistenceExtensions
{
    public static IServiceCollection AddBffPersistence(this IServiceCollection services, BffSettings settings)
    {
        services.AddDbContextFactory<BffDbContext>(options => BffDbContext.Configure(options, settings.ConnectionString));
        services.AddDataProtection().SetApplicationName("InkLink.Bff")
            .PersistKeysToDbContext<BffDbContext>().ProtectKeysWithCertificate(settings.LoadCertificate());
        return services;
    }
}
