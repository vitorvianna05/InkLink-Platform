using System.Diagnostics;
using InkLink.Identity.Api;
using InkLink.Identity.Application;
using InkLink.Identity.Infrastructure;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
builder.Services.AddIdentityInfrastructure(builder.Configuration);
builder.Services.AddScoped<GetMyProfile>();
builder.Services.AddScoped<ICurrentUser, UnavailableCurrentUser>();
builder.Services.AddOpenApi(options => options.AddSchemaTransformer<ProblemDetailsSchemaTransformer>());
builder.Services.AddSingleton<IProblemDetailsWriter, ApiProblemDetailsWriter>();
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    context.ProblemDetails.Extensions["traceId"] = Activity.Current?.TraceId.ToString() ?? context.HttpContext.TraceIdentifier;
    context.ProblemDetails.Extensions.TryAdd("code", "http." + context.ProblemDetails.Status);
});
builder.Services.AddExceptionHandler<SanitizedExceptionHandler>();
var app = builder.Build();
app.Use(async (context, next) =>
{
    var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
    context.Response.OnStarting(() =>
    {
        context.Response.Headers["X-Trace-Id"] = traceId;
        return Task.CompletedTask;
    });
    using var scope = app.Logger.BeginScope(new Dictionary<string, object> { ["traceId"] = traceId });
    var start = Stopwatch.GetTimestamp();
    try { await next(context); }
    finally
    {
        app.Logger.LogInformation("HTTP {Method} {Path} returned {StatusCode} in {ElapsedMs} ms",
            context.Request.Method, context.Request.Path.Value, context.Response.StatusCode,
            Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }
});
app.UseExceptionHandler();
app.UseStatusCodePages();
app.MapGet("/health/live", () => TypedResults.Ok(new HealthResponse("Healthy")))
    .WithSummary("Check process liveness")
    .WithDescription("Returns 200 while the process responds; does not query the database.")
    .ProducesProblem(StatusCodes.Status500InternalServerError);
app.MapGet("/health/ready", async (HealthCheckService health, CancellationToken cancellationToken) =>
{
    var report = await health.CheckHealthAsync(check => check.Tags.Contains("ready"), cancellationToken);
    return report.Status == HealthStatus.Healthy
        ? Results.Ok(new HealthResponse("Healthy"))
        : Results.Problem(statusCode: 503, title: "Service unavailable", extensions: new Dictionary<string, object?> { ["code"] = "identity.database_unavailable" });
})
    .WithSummary("Check Identity readiness")
    .WithDescription("Returns 200 when the profile table is readable, or 503 with code identity.database_unavailable when the database or schema is unavailable.")
    .Produces<HealthResponse>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
    .ProducesProblem(StatusCodes.Status500InternalServerError);
if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing")) app.MapOpenApi();
// The private profile transport and trusted caller adapter are introduced in P02.
app.Run();

public partial class Program;
internal sealed class UnavailableCurrentUser : ICurrentUser { public Guid? UserId => null; }
internal sealed class SanitizedExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        context.Response.StatusCode = 500;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = 500,
                Title = "An unexpected error occurred.",
                Extensions = { ["code"] = "internal_error" }
            }
        });
    }
}
