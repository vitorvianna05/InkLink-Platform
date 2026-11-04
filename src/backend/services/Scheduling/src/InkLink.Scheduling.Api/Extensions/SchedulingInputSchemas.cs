using InkLink.Scheduling.Application.Reservations;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace InkLink.Scheduling.Api.Extensions;

internal sealed class SchedulingInputSchemas : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken ct)
    {
        var type = context.JsonTypeInfo.Type;
        if (type == typeof(DateTimeOffset) || type == typeof(DateTimeOffset?))
        {
            schema.Type = type == typeof(DateTimeOffset?) ? JsonSchemaType.String | JsonSchemaType.Null : JsonSchemaType.String;
            schema.Format = "date-time";
            schema.Description = "Timestamp with an explicit UTC offset or Z.";
        }
        if (type == typeof(CompletionInput))
        {
            schema.Required ??= new HashSet<string>();
            schema.Required.Add("completedAt");
            if (schema.Properties?["completedAt"] is OpenApiSchema completed)
                completed.Description = "Between the reserved end and now; only the owning artist may complete a session after its end.";
            if (schema.Properties?["amount"] is OpenApiSchema amount)
            {
                amount.Minimum = "0.01"; amount.Maximum = "9999999.99"; amount.MultipleOf = 0.01m;
                amount.Description = "Positive BRL session value, at most two decimal places. Omit or use null for the reservation reference amount. Completion does not register a receipt.";
            }
        }
        if (type == typeof(ReserveInput))
            foreach (var name in new[] { "start", "end" })
                if (schema.Properties?[name] is OpenApiSchema instant)
                    instant.Description = "Artist-local offset, whole-minute precision, within the same available civil day. Start must be in the future and end must be later than start.";
        return Task.CompletedTask;
    }
}
