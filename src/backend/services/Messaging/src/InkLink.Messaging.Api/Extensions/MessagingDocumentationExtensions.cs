using InkLink.Messaging.Application.Conversations;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace InkLink.Messaging.Api.Extensions;

internal static class MessagingDocumentationExtensions
{
    public static IServiceCollection AddMessagingDocumentation(this IServiceCollection services)
    {
        services.AddOpenApi(options =>
        {
            options.AddSchemaTransformer<ProblemDetailsSchemaTransformer>();
            options.AddSchemaTransformer<MessageSchemaTransformer>();
            options.AddOperationTransformer<AuthenticationOpenApi>();
            options.AddOperationTransformer<MessageIdempotencyTransformer>();
        });
        return services;
    }
}

internal sealed class MessageSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        if (context.JsonTypeInfo.Type != typeof(MessageInput)) return Task.CompletedTask;
        schema.Required ??= new HashSet<string>();
        schema.Required.Add("body");
        if (schema.Properties?["body"] is OpenApiSchema body)
        {
            body.Type &= ~JsonSchemaType.Null;
            body.MinLength = 1; body.MaxLength = 4000; body.Pattern = @"\S";
            body.Description = "Nonblank message; leading and trailing whitespace is trimmed.";
        }
        return Task.CompletedTask;
    }
}

internal sealed class MessageIdempotencyTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        if (context.Description.HttpMethod != "POST") return Task.CompletedTask;
        operation.Parameters ??= [];
        operation.Parameters.Add(new OpenApiParameter { Name = "Idempotency-Key", In = ParameterLocation.Header, Required = true,
            Description = "Nonempty UUID scoped to author and conversation; retry with the same content.",
            Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" } });
        return Task.CompletedTask;
    }
}
