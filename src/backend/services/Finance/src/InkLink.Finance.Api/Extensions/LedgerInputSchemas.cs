using InkLink.Finance.Application.Ledger;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
namespace InkLink.Finance.Api.Extensions;

internal sealed class LedgerInputSchemas : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken ct)
    {
        var type = context.JsonTypeInfo.Type;
        var required = type == typeof(EntryInput) ? new[] { "kind", "description", "amount", "effectiveDate" }
            : type == typeof(ReversalInput) ? new[] { "reason" } : [];
        if (required.Length == 0) return Task.CompletedTask;
        schema.Required ??= new HashSet<string>(); schema.Required.UnionWith(required);
        foreach (var field in required)
        {
            if (schema.Properties?[field] is not OpenApiSchema property) continue;
            property.Type &= ~JsonSchemaType.Null;
            if (field is "description" or "reason")
            { property.MinLength = 1; property.MaxLength = 4000; property.Pattern = @"\S"; }
            if (field == "amount")
            { property.Minimum = "0.01"; property.Maximum = "9999999.99"; property.MultipleOf = 0.01m; }
            if (field == "kind") property.Description = "Receipt or Cost. Receipt requires engagementId owned by the artist.";
            if (field == "effectiveDate") property.Description = "Artist-local effective date, not later than today. Reversals restate the original date.";
        }
        return Task.CompletedTask;
    }
}
