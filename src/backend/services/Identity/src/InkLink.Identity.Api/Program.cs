using InkLink.Authentication;
using InkLink.Identity.Api;
using InkLink.Identity.Application;
using InkLink.Identity.Infrastructure;
using InkLink.Observability;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Diagnostics;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
builder.Services.AddIdentityInfrastructure(builder.Configuration);
builder.Services.AddScoped<GetMyProfile>();
builder.Services.AddScoped<UpdateMyProfile>();
builder.Services.AddScoped<ProvisionAccount>();
builder.Services.AddInkLinkAccessTokens(builder.Configuration, builder.Environment);
builder.Services.AddScoped<CallerContext>();
builder.Services.AddScoped<ICurrentUser>(provider => provider.GetRequiredService<CallerContext>());
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);
builder.Services.AddSingleton<IProblemDetailsWriter, ApiProblemDetailsWriter>();
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    context.ProblemDetails.Extensions["traceId"] = Activity.Current?.TraceId.ToString() ?? context.HttpContext.TraceIdentifier;
    context.ProblemDetails.Extensions.TryAdd("code", "http." + context.ProblemDetails.Status);
});
builder.Services.AddExceptionHandler<SanitizedExceptionHandler>();
builder.Services.AddOpenApi(options =>
{
    options.AddSchemaTransformer<ProblemDetailsSchemaTransformer>();
    options.AddOperationTransformer<AuthenticationOpenApi>();
});
var app = builder.Build();
app.UseCorrelatedRequestLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<CallerMiddleware>();
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
app.MapGet("/api/v1/profiles/me", async (GetMyProfile query, CallerContext caller, CancellationToken cancellationToken) =>
{
    var result = await query.ExecuteAsync(cancellationToken);
    return result.Status == ProfileStatus.Found
        ? Results.Ok(new MyProfileResponse(result.Profile!.Id, result.Profile.DisplayName, caller.IsArtist ? ["Client", "Artist"] : ["Client"]))
        : Results.Problem(statusCode: result.Status == ProfileStatus.Unauthenticated ? 401 : 404);
}).RequireAuthorization(AccessTokens.UserPolicy).WithMetadata(new CurrentAccountEndpoint()).Produces<MyProfileResponse>().ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(StatusCodes.Status500InternalServerError);
app.MapPut("/api/v1/profiles/me", async (RenameProfileRequest request, UpdateMyProfile command, CancellationToken cancellationToken) =>
{
    var result = await command.ExecuteAsync(request.DisplayName, cancellationToken);
    return result switch
    {
        UpdateProfileStatus.Updated => Results.NoContent(),
        UpdateProfileStatus.InvalidName => Results.ValidationProblem(new Dictionary<string, string[]> { ["displayName"] = ["Display name is required."] }),
        _ => Results.Problem(statusCode: result == UpdateProfileStatus.Unauthenticated ? 401 : 404)
    };
}).RequireAuthorization(AccessTokens.UserPolicy).WithMetadata(new CurrentAccountEndpoint()).Produces(204).ProducesValidationProblem().ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(StatusCodes.Status500InternalServerError);

app.Run();

public partial class IdentityProgram;
internal sealed class SanitizedExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var status = exception is BadHttpRequestException badRequest ? badRequest.StatusCode : 500;
        ProblemDetails problem = status == 400
            ? new HttpValidationProblemDetails(new Dictionary<string, string[]> { ["body"] = ["The request body has unknown fields or invalid values."] })
            : new ProblemDetails();
        problem.Status = status;
        problem.Title = status < 500 ? "Invalid request." : "An unexpected error occurred.";
        problem.Extensions["code"] = status < 500 ? "request.invalid" : "internal_error";
        context.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem });
    }
}
