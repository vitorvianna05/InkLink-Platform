using InkLink.Catalog.Application.Publishing;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace InkLink.Catalog.Api;

internal sealed class CatalogInputSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        string[] required = context.JsonTypeInfo.Type == typeof(ArtistInput)
            ? ["name", "studio", "city", "biography", "styles", "startingPrice"]
            : context.JsonTypeInfo.Type == typeof(ArtworkInput) ? ["title", "imageUrl", "alternativeText", "style"] : [];
        if (required.Length == 0) return Task.CompletedTask;
        schema.Required ??= new HashSet<string>();
        schema.Required.UnionWith(required);
        foreach (var field in required)
        {
            if (schema.Properties?.TryGetValue(field, out var value) == true && value is OpenApiSchema property)
                property.Type &= ~JsonSchemaType.Null;
        }
        return Task.CompletedTask;
    }
}
