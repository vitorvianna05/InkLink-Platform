using InkLink.Authentication;
using InkLink.Bff;
using InkLink.Observability;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using System.Globalization;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
builder.Logging.AddFilter("Yarp.ReverseProxy.Forwarder.HttpForwarder", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.AspNetCore.Diagnostics", LogLevel.Critical);
builder.Logging.AddFilter("Microsoft.AspNetCore.Authentication", LogLevel.Critical);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Critical);
builder.Logging.AddFilter("Microsoft.AspNetCore.DataProtection", LogLevel.Critical);
var settings = BffSettings.Load(builder.Configuration, builder.Environment);
builder.Services.AddDbContextFactory<BffDbContext>(options => BffDbContext.Configure(options, settings.ConnectionString));
builder.Services.AddDataProtection().SetApplicationName("InkLink.Bff")
    .PersistKeysToDbContext<BffDbContext>().ProtectKeysWithCertificate(settings.LoadCertificate());
builder.Services.AddSingleton<ITicketStore, PostgresTicketStore>();
builder.Services.AddAuthentication(options =>
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
builder.Services.AddOptions<CookieAuthenticationOptions>("session").Configure<ITicketStore>((options, store) => options.SessionStore = store);
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "__Host-inklink-csrf";
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
});
builder.Services.AddReverseProxy().LoadFromMemory(
    [new RouteConfig { RouteId = "web-api", ClusterId = "gateway", Match = new RouteMatch { Path = "/api/v1/{**remainder}" }, AuthorizationPolicy = "default" }],
    [new ClusterConfig { ClusterId = "gateway", Destinations = new Dictionary<string, DestinationConfig> { ["gateway"] = new() { Address = settings.GatewayAddress } },
        HttpRequest = new() { ActivityTimeout = TimeSpan.FromSeconds(30) } }])
    .AddTransforms(context =>
    {
        context.AddRequestHeaderRemove("Cookie");
        context.AddRequestHeaderRemove("X-CSRF-TOKEN");
        context.AddRequestHeaderRemove("X-User-Id");
        context.AddRequestHeaderRemove("X-Roles");
        context.AddRequestTransform(async transform =>
        {
            var token = await transform.HttpContext.GetTokenAsync("session", "access_token");
            transform.ProxyRequest.Headers.Authorization = new("Bearer", token);
        });
    });
var app = builder.Build();
app.UseCorrelatedRequestLogging();
app.UseExceptionHandler(handler => handler.Run(context => ApiErrors.WriteAsync(context, 500, "internal_error")));
app.UseStatusCodePages(context => ApiErrors.WriteAsync(context.HttpContext, context.HttpContext.Response.StatusCode, "http." + context.HttpContext.Response.StatusCode));
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    await next(context);
});
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true && context.Request.Method is not ("GET" or "HEAD" or "OPTIONS"))
    {
        try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
        catch (AntiforgeryValidationException) { await ApiErrors.WriteAsync(context, 400, "csrf.invalid"); return; }
    }
    await next(context);
});
app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" }));
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
app.MapReverseProxy();
app.Run();

public partial class BffProgram;
