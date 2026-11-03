using InkLink.Catalog.Infrastructure;
using InkLink.Catalog.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace InkLink.Catalog.Admin.Extensions;

internal static class CatalogAdministrationServicesExtensions
{
    public static IServiceCollection AddCatalogMaintenance(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddLogging();
        services.AddCatalogInfrastructure(configuration);
        services.AddScoped<CatalogSearchMaintenance>();
        return services;
    }
}
