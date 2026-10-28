using InkLink.Authentication;
using Microsoft.AspNetCore.Antiforgery;

namespace InkLink.Bff.Extensions;

internal static class BffAntiforgeryExtensions
{
    public static IServiceCollection AddBffAntiforgery(this IServiceCollection services)
    {
        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.Cookie.Name = "__Host-inklink-csrf";
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
        });
        return services;
    }

    public static WebApplication UseBffAntiforgery(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (context.User.Identity?.IsAuthenticated == true && context.Request.Method is not ("GET" or "HEAD" or "OPTIONS"))
            {
                try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
                catch (AntiforgeryValidationException) { await ApiErrors.WriteAsync(context, 400, "csrf.invalid"); return; }
            }
            await next(context);
        });
        return app;
    }
}
