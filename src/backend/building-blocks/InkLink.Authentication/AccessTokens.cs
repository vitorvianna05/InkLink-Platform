using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace InkLink.Authentication;

public sealed class AccessTokenSettings
{
    public string Authority { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string[] UserClients { get; set; } = [];
    public string[] TechnicalClients { get; set; } = [];
    public bool AllowHttpMetadata { get; set; }
}

public static class AccessTokens
{
    public const string UserPolicy = "inklink-user";
    public const string TechnicalPolicy = "identity-profile-reader";
    public static IServiceCollection AddInkLinkAccessTokens(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var settings = configuration.GetSection("Authentication").Get<AccessTokenSettings>() ?? new();
        if (!Uri.TryCreate(settings.Authority, UriKind.Absolute, out var authority)
            || (settings.AllowHttpMetadata && !(environment.IsDevelopment() || environment.IsEnvironment("Testing")))
            || string.IsNullOrWhiteSpace(settings.Audience) || settings.UserClients.Length == 0
            || (authority.Scheme != "https" && !(authority.Scheme == "http" && settings.AllowHttpMetadata
                && (environment.IsDevelopment() || environment.IsEnvironment("Testing")))))
            throw new InvalidOperationException("Authentication configuration requires a valid authority, audience and user clients. HTTP metadata is restricted to explicit local/test configuration.");
        services.AddSingleton(settings);
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.Authority = settings.Authority;
            options.Audience = settings.Audience;
            options.RequireHttpsMetadata = !settings.AllowHttpMetadata;
            options.MapInboundClaims = false;
            options.IncludeErrorDetails = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                RequireExpirationTime = true,
                RequireSignedTokens = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = settings.Authority,
                ValidAudience = settings.Audience,
                ClockSkew = TimeSpan.FromSeconds(5),
                NameClaimType = "preferred_username"
            };
            options.Events = new JwtBearerEvents
            {
                OnAuthenticationFailed = context =>
                {
                    context.HttpContext.RequestServices.GetRequiredService<ILogger<AccessTokenSettings>>()
                        .LogWarning("Access token rejected: {FailureType}", context.Exception.GetType().Name);
                    return Task.CompletedTask;
                },
                OnTokenValidated = context =>
                {
                    if (context.Principal?.FindFirstValue("typ") != "Bearer"
                        || string.IsNullOrWhiteSpace(context.Principal.FindFirstValue("sub")))
                    {
                        context.HttpContext.RequestServices.GetRequiredService<ILogger<AccessTokenSettings>>()
                            .LogWarning("Access token rejected: required subject or access-token type is missing.");
                        context.Fail("An access token is required.");
                    }
                    return Task.CompletedTask;
                },
                OnChallenge = async context =>
                {
                    context.HandleResponse();
                    context.Response.Headers.WWWAuthenticate = "Bearer";
                    await ApiErrors.WriteAsync(context.HttpContext, 401, "authentication.required");
                },
                OnForbidden = context => ApiErrors.WriteAsync(context.HttpContext, 403, "authorization.denied")
            };
        });
        services.AddAuthorization(options =>
        {
            options.AddPolicy(UserPolicy, policy => policy.RequireAuthenticatedUser()
                .RequireAssertion(context => IsUser(context.User, settings)));

        });
        return services;
    }

    public static bool IsUser(ClaimsPrincipal user, AccessTokenSettings settings) =>
        user.Identity?.IsAuthenticated == true && user.FindFirstValue("inklink_actor") == "user"
        && settings.UserClients.Contains(user.FindFirstValue("azp"), StringComparer.Ordinal);
}

public static class ApiErrors
{
    public static Task WriteAsync(HttpContext context, int status, string code) => Results.Json(new ProblemDetails
    {
        Type = "about:blank",
        Status = status,
        Title = status switch { 400 => "Invalid request", 401 => "Authentication required", 403 => "Access denied", 502 => "Upstream unavailable", _ => "Request failed" },
        Extensions = { ["code"] = code, ["traceId"] = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier }
    }, statusCode: status, contentType: "application/problem+json").ExecuteAsync(context);
}
