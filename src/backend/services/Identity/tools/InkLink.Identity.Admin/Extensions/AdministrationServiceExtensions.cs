using InkLink.Identity.Application;
using InkLink.Identity.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace InkLink.Identity.Admin.Extensions;

internal static class AdministrationServiceExtensions
{
    public static IServiceCollection AddIdentityAdministration(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddLogging();
        services.AddScoped<ActivateArtist>();
        services.AddIdentityInfrastructure(configuration);
        return services;
    }
}
