using InkLink.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.JsonWebTokens;

namespace InkLink.Bff.Extensions;

internal static class BffAuthenticationExtensions
{
    public static IServiceCollection AddBffAuthentication(this IServiceCollection services, BffSettings settings)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ITicketStore, PostgresTicketStore>();
        services.AddSingleton<BffSessionRenewal>();
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = "session";
            options.DefaultSignInScheme = "session";
            options.DefaultChallengeScheme = "session";
        }).AddCookie("session", options =>
        {
            options.Cookie.Name = "__Host-inklink-session";
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.Path = "/";
            // Validade deslizante e limite total são controlados por BffSessionRenewal (decisão 0019).
            options.SlidingExpiration = false;
            options.ExpireTimeSpan = BffSessionRenewal.IdleTimeout;
            options.Events.OnRedirectToLogin = context => ApiErrors.WriteAsync(context.HttpContext, 401, "session.required");
            options.Events.OnRedirectToAccessDenied = context => ApiErrors.WriteAsync(context.HttpContext, 403, "authorization.denied");
            options.Events.OnValidatePrincipal = context => context.HttpContext.RequestServices.GetRequiredService<BffSessionRenewal>().ValidateAsync(context);
        }).AddOpenIdConnect("oidc", options =>
        {
            options.Authority = settings.Authority;
            options.ClientId = settings.ClientId;
            options.ClientSecret = settings.ClientSecret;
            options.RequireHttpsMetadata = !settings.AllowHttpMetadata;
            options.ResponseType = "code";
            options.ResponseMode = "query";
            options.UsePkce = true;
            options.SaveTokens = true;
            options.MapInboundClaims = false;
            options.TokenValidationParameters.NameClaimType = "name";
            options.Scope.Clear();
            options.Scope.Add("openid");
            options.Scope.Add("profile");
            options.Events.OnTicketReceived = context =>
            {
                if (context.Properties is null) { context.Fail("Login properties are required."); return Task.CompletedTask; }
                var accessToken = context.Properties.GetTokenValue("access_token");
                if (string.IsNullOrWhiteSpace(accessToken) || !new JsonWebTokenHandler().CanReadToken(accessToken))
                {
                    context.Fail("A JWT access token is required.");
                    return Task.CompletedTask;
                }
                if (string.IsNullOrWhiteSpace(context.Properties.GetTokenValue("refresh_token")))
                {
                    context.Fail("A refresh token is required for the renewable session.");
                    return Task.CompletedTask;
                }
                var time = context.HttpContext.RequestServices.GetRequiredService<TimeProvider>();
                var now = time.GetUtcNow();
                if (new DateTimeOffset(new JsonWebToken(accessToken).ValidTo) <= now) { context.Fail("Access token has expired."); return Task.CompletedTask; }
                // 30 minutos sem uso e 8 horas no total; o access token é renovado no servidor (decisão 0019).
                BffSessionRenewal.Start(context.Properties, now);
                return Task.CompletedTask;
            };
            options.Events.OnRemoteFailure = context =>
            {
                context.HandleResponse();
                // Login iniciado pelo website com state válido: volta à página de login com o aviso e o destino.
                if (context.Properties?.Items.TryGetValue(BffSessionEndpointExtensions.WebReturnItem, out var returnUrl) == true
                    && returnUrl is not null && BffReturnUrl.IsLocal(returnUrl))
                {
                    context.Response.Redirect(BffReturnUrl.Resolve(settings, "/entrar?erro=login&destino=" + Uri.EscapeDataString(returnUrl)));
                    return Task.CompletedTask;
                }
                return ApiErrors.WriteAsync(context.HttpContext, 401, "login.failed");
            };
            options.Events.OnRedirectToIdentityProviderForSignOut = context =>
            {
                context.ProtocolMessage.IdTokenHint = context.Properties.GetParameter<string>("id_token_hint");
                return Task.CompletedTask;
            };
        });
        services.AddOptions<CookieAuthenticationOptions>("session").Configure<ITicketStore, TimeProvider>((options, store, time) =>
        {
            options.SessionStore = store;
            options.TimeProvider = time;
        });
        services.AddOptions<OpenIdConnectOptions>("oidc").Configure<TimeProvider>((options, time) => options.TimeProvider = time);
        services.AddAuthorization();
        return services;
    }
}
