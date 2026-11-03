using InkLink.Engagements.Application.Negotiations;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace InkLink.Engagements.Api.Extensions;

internal static class EngagementsDocumentationExtensions
{
    public static IServiceCollection AddEngagementsDocumentation(this IServiceCollection services)
    {
        services.AddOpenApi(options =>
        {
            options.AddSchemaTransformer<ProblemDetailsSchemaTransformer>();
            options.AddSchemaTransformer<EngagementInputSchemaTransformer>();
            options.AddOperationTransformer<AuthenticationOpenApi>();
            options.AddOperationTransformer<IdempotencyOpenApi>();
        });
        return services;
    }

    public static WebApplication MapEngagementsDocumentation(this WebApplication app)
    {
        if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing")) app.MapOpenApi();
        return app;
    }
}

internal sealed class EngagementInputSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        string[] fields = context.JsonTypeInfo.Type == typeof(RequestInput) ? ["artistId", "idea", "bodyRegion", "approximateSizeCm"]
            : context.JsonTypeInfo.Type == typeof(ProposalInput) ? ["description", "amount"] : [];
        if (fields.Length == 0) return Task.CompletedTask;
        schema.Required ??= new HashSet<string>();
        schema.Required.UnionWith(fields);
        foreach (var field in fields)
            if (schema.Properties?.TryGetValue(field, out var value) == true && value is OpenApiSchema property)
                property.Type &= ~JsonSchemaType.Null;
        if (context.JsonTypeInfo.Type == typeof(RequestInput))
        {
            Text(schema, "idea", 4000);
            Text(schema, "bodyRegion", 120);
            Number(schema, "approximateSizeCm", "300", "Approximate size in centimeters.");
            Id(schema, "artistId");
            Id(schema, "flashId");
        }
        else
        {
            Text(schema, "description", 4000);
            Number(schema, "amount", "9999999.99", "Positive proposal amount in BRL; does not charge or reserve a time slot.");
        }
        return Task.CompletedTask;
    }

    private static void Text(OpenApiSchema schema, string field, int maximum)
    {
        if (schema.Properties?[field] is not OpenApiSchema property) return;
        property.MinLength = 1;
        property.MaxLength = maximum;
        property.Pattern = @"\S";
        property.Description = "Nonblank text. Leading/trailing whitespace is trimmed; maximum length applies to the input before trimming.";
    }

    private static void Number(OpenApiSchema schema, string field, string maximum, string description)
    {
        if (schema.Properties?[field] is not OpenApiSchema property) return;
        property.Minimum = "0.01";
        property.Maximum = maximum;
        property.MultipleOf = 0.01m;
        property.Description = description + " At most two decimal places.";
    }

    private static void Id(OpenApiSchema schema, string field)
    {
        if (schema.Properties?[field] is not OpenApiSchema property) return;
        property.Format = "uuid";
        property.Pattern = "^(?!00000000-0000-0000-0000-000000000000$)[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$";
        property.Description = "Nonempty UUID in canonical hyphenated format.";
    }
}

internal sealed class IdempotencyOpenApi : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        if (context.Description.HttpMethod != "POST") return Task.CompletedTask;
        operation.Parameters ??= [];
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "Idempotency-Key",
            In = ParameterLocation.Header,
            Required = true,
            Description = "Nonempty UUID. Same key and normalized payload repeat the result; a different payload returns 409.",
            Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" }
        });
        return Task.CompletedTask;
    }
}
