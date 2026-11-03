using InkLink.Authentication;
using InkLink.Catalog.Application.Accounts;

namespace InkLink.Catalog.Api.Extensions;

internal static class CatalogAuthenticationExtensions
{
    public static IServiceCollection AddCatalogAuthentication(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddInkLinkAccessTokens(configuration, environment);
        services.AddScoped<CatalogCaller>();
        services.AddScoped<ICurrentCatalogAccount>(provider => provider.GetRequiredService<CatalogCaller>());
        return services;
    }

    public static WebApplication UseCatalogAuthentication(this WebApplication app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<CatalogCallerMiddleware>();
        return app;
    }

    public static RouteHandlerBuilder RequireCatalogAccount(this RouteHandlerBuilder endpoint) =>
        endpoint.RequireAuthorization(AccessTokens.UserPolicy).WithMetadata(new CatalogAccountEndpoint());
}
