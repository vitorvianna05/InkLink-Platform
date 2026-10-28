using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;

namespace InkLink.Bff.Extensions;

internal static class BffSessionEndpointExtensions
{
    public static IEndpointRouteBuilder MapBffSessionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/bff/login", (HttpContext context) => context.ChallengeAsync("oidc", new AuthenticationProperties { RedirectUri = "/bff/session" }));
        app.MapGet("/bff/session", (HttpContext context) => Results.Ok(new { authenticated = true, name = context.User.Identity?.Name }))
            .RequireAuthorization();
        app.MapGet("/bff/antiforgery", (IAntiforgery antiforgery, HttpContext context) => Results.Ok(new { requestToken = antiforgery.GetAndStoreTokens(context).RequestToken }))
            .RequireAuthorization();
        app.MapPost("/bff/logout", async (HttpContext context) =>
        {
            var idToken = await context.GetTokenAsync("session", "id_token");
            await context.SignOutAsync("session");
            var properties = new AuthenticationProperties { RedirectUri = "/" };
            properties.SetParameter("id_token_hint", idToken);
            await context.SignOutAsync("oidc", properties);
        }).RequireAuthorization();
        return app;
    }
}
