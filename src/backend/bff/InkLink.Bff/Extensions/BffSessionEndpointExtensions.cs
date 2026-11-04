using InkLink.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;

namespace InkLink.Bff.Extensions;

internal static class BffSessionEndpointExtensions
{
    /// <summary>Marca o login iniciado pelo website, para que falhas do provedor também voltem a ele.</summary>
    internal const string WebReturnItem = "inklink.web-return";

    public static IEndpointRouteBuilder MapBffSessionEndpoints(this IEndpointRouteBuilder app, BffSettings settings)
    {
        // Sem returnUrl, o contrato original termina em /bff/session (JSON). Com returnUrl, o navegador
        // volta ao website já autenticado; somente caminhos locais são aceitos (sem redirecionamento aberto).
        app.MapGet("/bff/login", (HttpContext context, string? returnUrl) =>
        {
            if (returnUrl is null)
                return context.ChallengeAsync("oidc", new AuthenticationProperties { RedirectUri = "/bff/session" });
            if (!BffReturnUrl.IsLocal(returnUrl))
                return ApiErrors.WriteAsync(context, 400, "login.return_url_invalid");
            var properties = new AuthenticationProperties { RedirectUri = BffReturnUrl.Resolve(settings, returnUrl) };
            properties.Items[WebReturnItem] = returnUrl;
            return context.ChallengeAsync("oidc", properties);
        });
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

internal static class BffReturnUrl
{
    /// <summary>Caminho local do website: começa com uma única barra, sem barra invertida ou caracteres de controle.</summary>
    public static bool IsLocal(string value) =>
        value.Length is > 0 and <= 2048
        && value[0] == '/'
        && (value.Length == 1 || (value[1] != '/' && value[1] != '\\'))
        && !value.Contains('\\')
        && !value.Any(char.IsControl);

    public static string Resolve(BffSettings settings, string path) =>
        string.IsNullOrEmpty(settings.WebOrigin) ? path : settings.WebOrigin + path;
}
