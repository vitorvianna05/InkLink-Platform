using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace InkLink.Scheduling.Api.Extensions;

internal static class SchedulingDocumentationExtensions
{
    public static IServiceCollection AddSchedulingDocumentation(this IServiceCollection services)
    {
        services.AddOpenApi(o => { o.AddSchemaTransformer<ProblemDetailsSchemaTransformer>(); o.AddOperationTransformer<AuthenticationOpenApi>(); o.AddOperationTransformer<ReservationIdempotency>(); });
        return services;
    }
    public static WebApplication MapSchedulingDocumentation(this WebApplication app)
    {
        if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing")) app.MapOpenApi();
        return app;
    }
}
internal sealed class ReservationIdempotency : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken ct)
    {
        if (context.Description.HttpMethod != "POST") return Task.CompletedTask;
        operation.Parameters ??= [];
        operation.Parameters.Add(new OpenApiParameter { Name = "Idempotency-Key", In = ParameterLocation.Header, Required = true, Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" } });
        return Task.CompletedTask;
    }
}
