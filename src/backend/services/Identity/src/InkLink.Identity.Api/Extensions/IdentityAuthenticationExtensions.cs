using InkLink.Authentication;
using InkLink.Identity.Application;

namespace InkLink.Identity.Api.Extensions;

internal static class IdentityAuthenticationExtensions
{
    public static IServiceCollection AddIdentityAuthentication(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddInkLinkAccessTokens(configuration, environment);
        services.AddScoped<CallerContext>();
        services.AddScoped<ICurrentUser>(provider => provider.GetRequiredService<CallerContext>());
        return services;
    }

    public static WebApplication UseIdentityAuthentication(this WebApplication app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<CallerMiddleware>();
        return app;
    }
}
