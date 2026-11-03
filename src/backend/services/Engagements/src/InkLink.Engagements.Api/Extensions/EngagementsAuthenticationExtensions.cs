using InkLink.Authentication;
using InkLink.Engagements.Application.Accounts;

namespace InkLink.Engagements.Api.Extensions;

internal static class EngagementsAuthenticationExtensions
{
    public static IServiceCollection AddEngagementsAuthentication(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddInkLinkAccessTokens(configuration, environment);
        services.AddScoped<EngagementsCaller>();
        services.AddScoped<ICurrentEngagementsAccount>(provider => provider.GetRequiredService<EngagementsCaller>());
        return services;
    }

    public static WebApplication UseEngagementsAuthentication(this WebApplication app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<EngagementsCallerMiddleware>();
        return app;
    }

    public static RouteHandlerBuilder RequireEngagementsAccount(this RouteHandlerBuilder endpoint) =>
        endpoint.RequireAuthorization(AccessTokens.UserPolicy).WithMetadata(new EngagementsAccountEndpoint());
}
