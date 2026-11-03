using InkLink.Catalog.Infrastructure;
using InkLink.Catalog.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace InkLink.Catalog.Admin.Extensions;

internal static class CatalogMaintenanceExtensions
{
    public static async Task<int> RunCatalogMaintenanceAsync(this string[] args)
    {
        if (args.Length != 1 || args[0] != "rebuild-search")
        {
            Console.Error.WriteLine("Usage: rebuild-search. Configure CatalogDatabase__ConnectionString with the catalog owner credential.");
            return 2;
        }
        try
        {
            var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
            var services = new ServiceCollection().AddCatalogMaintenance(configuration);
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var count = await scope.ServiceProvider.GetRequiredService<CatalogSearchMaintenance>().RebuildAsync(CancellationToken.None);
            Console.WriteLine($"Search rebuilt from {count} catalog profiles.");
            return 0;
        }
        catch (Exception)
        {
            Console.Error.WriteLine("Search rebuild failed. Check catalog owner configuration and database availability.");
            return 1;
        }
    }
}
