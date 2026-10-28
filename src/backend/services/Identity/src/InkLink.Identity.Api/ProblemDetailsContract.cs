using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace InkLink.Identity.Api;

// Errors have one JSON contract even when a client requests another media type.
internal sealed class ApiProblemDetailsWriter(IOptions<ProblemDetailsOptions> options) : IProblemDetailsWriter
{
    public bool CanWrite(ProblemDetailsContext context) => true;

    public ValueTask WriteAsync(ProblemDetailsContext context)
    {
        var problem = context.ProblemDetails;
        problem.Status ??= context.HttpContext.Response.StatusCode;
        problem.Title ??= ReasonPhrases.GetReasonPhrase(problem.Status.Value);
        problem.Type ??= "about:blank";
        options.Value.CustomizeProblemDetails?.Invoke(context);
        context.HttpContext.Response.StatusCode = problem.Status.Value;
        context.HttpContext.Response.ContentType = "application/problem+json";
        var json = context.HttpContext.RequestServices.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;
        return new(JsonSerializer.SerializeAsync(context.HttpContext.Response.Body, problem, problem.GetType(), json,
            context.HttpContext.RequestAborted));
    }
}

internal sealed class ProblemDetailsSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        if (!typeof(ProblemDetails).IsAssignableFrom(context.JsonTypeInfo.Type)) return Task.CompletedTask;
        schema.Properties ??= new Dictionary<string, IOpenApiSchema>();
        schema.Properties["code"] = new OpenApiSchema
        {
            Type = JsonSchemaType.String,
            Description = "Stable error code, such as identity.database_unavailable or internal_error."
        };
        schema.Properties["traceId"] = new OpenApiSchema
        {
            Type = JsonSchemaType.String,
            Description = "Request correlation identifier, also returned in X-Trace-Id."
        };
        schema.Required ??= new HashSet<string>();
        schema.Required.UnionWith(["type", "title", "status", "code", "traceId"]);
        return Task.CompletedTask;
    }
}
