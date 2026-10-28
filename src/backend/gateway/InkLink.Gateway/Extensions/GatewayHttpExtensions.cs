using InkLink.Authentication;

namespace InkLink.Gateway.Extensions;

internal static class GatewayHttpExtensions
{
    public static WebApplication UseGatewayHttpPolicies(this WebApplication app)
    {
        app.UseExceptionHandler(handler => handler.Run(context => ApiErrors.WriteAsync(context, 500, "internal_error")));
        app.UseStatusCodePages(context => ApiErrors.WriteAsync(context.HttpContext, context.HttpContext.Response.StatusCode, "http." + context.HttpContext.Response.StatusCode));
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            await next(context);
        });
        return app;
    }

    public static IEndpointRouteBuilder MapGatewayHealth(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" }));
        return app;
    }
}
