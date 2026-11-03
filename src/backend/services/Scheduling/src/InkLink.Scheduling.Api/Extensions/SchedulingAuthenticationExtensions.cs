using InkLink.Authentication;
using InkLink.Scheduling.Application.Accounts;

namespace InkLink.Scheduling.Api.Extensions;

internal static class SchedulingAuthenticationExtensions
{
    public static IServiceCollection AddSchedulingAuthentication(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddInkLinkAccessTokens(configuration, environment);
        services.AddScoped<SchedulingCaller>();
        services.AddScoped<ICurrentSchedulingAccount>(provider => provider.GetRequiredService<SchedulingCaller>());
        return services;
    }

    public static WebApplication UseSchedulingAuthentication(this WebApplication app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<SchedulingCallerMiddleware>();
        return app;
    }

    public static RouteHandlerBuilder RequireSchedulingAccount(this RouteHandlerBuilder endpoint) =>
        endpoint.RequireAuthorization(AccessTokens.UserPolicy).WithMetadata(new SchedulingAccountEndpoint());
}
