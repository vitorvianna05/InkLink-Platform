using InkLink.Authentication;
using InkLink.Messaging.Application.Accounts;

namespace InkLink.Messaging.Api.Extensions;

internal static class MessagingAuthenticationExtensions
{
    public static IServiceCollection AddMessagingAuthentication(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddInkLinkAccessTokens(configuration, environment);
        services.AddScoped<MessagingCaller>();
        services.AddScoped<ICurrentMessagingAccount>(provider => provider.GetRequiredService<MessagingCaller>());
        return services;
    }

    public static WebApplication UseMessagingAuthentication(this WebApplication app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<MessagingCallerMiddleware>();
        return app;
    }

    public static RouteHandlerBuilder RequireMessagingAccount(this RouteHandlerBuilder endpoint) =>
        endpoint.RequireAuthorization(AccessTokens.UserPolicy).WithMetadata(new MessagingAccountEndpoint());
}
