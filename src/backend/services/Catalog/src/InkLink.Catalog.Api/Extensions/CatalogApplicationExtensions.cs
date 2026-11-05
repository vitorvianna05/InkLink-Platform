using InkLink.Catalog.Application.Accounts;
using InkLink.Catalog.Application.Favorites;
using InkLink.Catalog.Application.Profiles;
using InkLink.Catalog.Application.Publishing;
using InkLink.Catalog.Application.Queries;

namespace InkLink.Catalog.Api.Extensions;

internal static class CatalogApplicationExtensions
{
    public static IServiceCollection AddCatalogUseCases(this IServiceCollection services)
    {
        services.AddScoped<ReadCatalog>();
        services.AddScoped<PublishArtist>();
        services.AddScoped<ManageArtworks>();
        services.AddScoped<ManageFavorites>();
        services.AddScoped<ResolveCatalogAccount>();
        services.AddScoped<ProjectArtistPhoto>();
        return services;
    }
}
