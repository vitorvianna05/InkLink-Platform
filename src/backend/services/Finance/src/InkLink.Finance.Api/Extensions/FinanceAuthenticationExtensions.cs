using InkLink.Authentication;
using InkLink.Finance.Application.Accounts;

namespace InkLink.Finance.Api.Extensions;

internal static class FinanceAuthenticationExtensions
{
    public static IServiceCollection AddFinanceAuthentication(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddInkLinkAccessTokens(configuration, environment);
        services.AddScoped<FinanceCaller>();
        services.AddScoped<ICurrentFinanceAccount>(provider => provider.GetRequiredService<FinanceCaller>());
        return services;
    }

    public static WebApplication UseFinanceAuthentication(this WebApplication app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<FinanceCallerMiddleware>();
        return app;
    }

    public static RouteHandlerBuilder RequireFinanceAccount(this RouteHandlerBuilder endpoint) =>
        endpoint.RequireAuthorization(AccessTokens.UserPolicy).WithMetadata(new FinanceAccountEndpoint());
}
