using System.Globalization;
using InkLink.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.IdentityModel.JsonWebTokens;

namespace InkLink.Bff.Extensions;

internal static class BffAuthenticationExtensions
{
    public static IServiceCollection AddBffAuthentication(this IServiceCollection services, BffSettings settings)
    {
        services.AddSingleton<ITicketStore, PostgresTicketStore>();
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
            options.SlidingExpiration = false;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
            options.Events.OnRedirectToLogin = context => ApiErrors.WriteAsync(context.HttpContext, 401, "session.required");
            options.Events.OnRedirectToAccessDenied = context => ApiErrors.WriteAsync(context.HttpContext, 403, "authorization.denied");
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
                var maximum = DateTimeOffset.UtcNow.AddMinutes(5);
                var value = context.Properties.GetTokenValue("expires_at");
                if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expiration))
                {
                    context.Fail("Token expiry is required.");
                    return Task.CompletedTask;
                }
                var accessToken = context.Properties.GetTokenValue("access_token");
                if (string.IsNullOrWhiteSpace(accessToken) || !new JsonWebTokenHandler().CanReadToken(accessToken))
                {
                    context.Fail("A JWT access token is required.");
                    return Task.CompletedTask;
                }
                var tokenExpiration = new DateTimeOffset(new JsonWebToken(accessToken).ValidTo);
                if (tokenExpiration <= DateTimeOffset.UtcNow) { context.Fail("Access token has expired."); return Task.CompletedTask; }
                if (tokenExpiration < expiration) expiration = tokenExpiration;
                context.Properties.ExpiresUtc = expiration < maximum ? expiration : maximum;
                context.Properties.AllowRefresh = false;
                return Task.CompletedTask;
            };
            options.Events.OnRemoteFailure = context =>
            {
                context.HandleResponse();
                return ApiErrors.WriteAsync(context.HttpContext, 401, "login.failed");
            };
            options.Events.OnRedirectToIdentityProviderForSignOut = context =>
            {
                context.ProtocolMessage.IdTokenHint = context.Properties.GetParameter<string>("id_token_hint");
                return Task.CompletedTask;
            };
        });
        services.AddOptions<CookieAuthenticationOptions>("session").Configure<ITicketStore>((options, store) => options.SessionStore = store);
        services.AddAuthorization();
        return services;
    }
}
