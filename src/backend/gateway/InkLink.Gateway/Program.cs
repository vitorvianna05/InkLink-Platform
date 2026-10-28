using InkLink.Authentication;
using InkLink.Observability;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
builder.Logging.AddFilter("Yarp.ReverseProxy.Forwarder.HttpForwarder", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.AspNetCore.Diagnostics", LogLevel.Critical);
builder.Logging.AddFilter("Microsoft.AspNetCore.Authentication", LogLevel.Critical);
builder.Services.AddInkLinkAccessTokens(builder.Configuration, builder.Environment);
var destination = builder.Configuration["Gateway:IdentityAddress"];
if (!Uri.TryCreate(destination, UriKind.Absolute, out var target) || (target.Scheme is not ("https" or "http")) || (target.Scheme != "https" && !builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing")))
    throw new InvalidOperationException("Gateway requires a valid Identity address; HTTPS is mandatory outside local/test environments.");
builder.Services.AddReverseProxy().LoadFromMemory(
    [new RouteConfig { RouteId = "identity-user", ClusterId = "identity", Match = new RouteMatch { Path = "/api/v1/profiles/{**remainder}" }, AuthorizationPolicy = "default" },
     new RouteConfig { RouteId = "identity-technical", ClusterId = "identity", Match = new RouteMatch { Path = "/api/v1/internal/profiles/{**remainder}" }, AuthorizationPolicy = "default" }],
    [new ClusterConfig { ClusterId = "identity", Destinations = new Dictionary<string, DestinationConfig> { ["identity"] = new() { Address = destination! } },
        HttpRequest = new() { ActivityTimeout = TimeSpan.FromSeconds(30) } }])
    .AddTransforms(context =>
    {
        context.AddRequestHeaderRemove("Cookie");
        context.AddRequestHeaderRemove("X-User-Id");
        context.AddRequestHeaderRemove("X-Roles");
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
app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" }));
app.MapReverseProxy();
app.Run();

public partial class GatewayProgram;
