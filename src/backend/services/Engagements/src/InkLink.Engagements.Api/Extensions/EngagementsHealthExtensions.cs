using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace InkLink.Engagements.Api.Extensions;

internal static class EngagementsHealthExtensions
{
    public static IEndpointRouteBuilder MapEngagementsHealth(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health/live", () => TypedResults.Ok(new HealthResponse("Healthy")))
            .WithSummary("Check process liveness")
            .WithDescription("Returns 200 while the process responds; does not query the database.")
            .ProducesProblem(StatusCodes.Status500InternalServerError);
        app.MapGet("/health/ready", async (HealthCheckService health, CancellationToken cancellationToken) =>
        {
            var report = await health.CheckHealthAsync(check => check.Tags.Contains("ready"), cancellationToken);
            return report.Status == HealthStatus.Healthy
                ? Results.Ok(new HealthResponse("Healthy"))
                : Results.Problem(statusCode: 503, title: "Service unavailable", extensions: new Dictionary<string, object?> { ["code"] = "engagements.database_unavailable" });
        })
            .WithSummary("Check Engagements readiness")
            .WithDescription("Returns 200 when the engagements schema is readable, or 503 with code engagements.database_unavailable when the database or schema is unavailable.")
            .Produces<HealthResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status500InternalServerError);
        return app;
    }
}
