using System.Text.Json;
using System.Text.Json.Serialization;

namespace InkLink.Scheduling.Api.Extensions;

internal sealed class ExplicitOffsetConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String) throw new JsonException("A timestamp string with explicit offset is required.");
        var value = reader.GetString();
        if (value is null || !(value.EndsWith('Z') || (value.Length >= 6 && value[^3] == ':' && (value[^6] == '+' || value[^6] == '-')))
            || !reader.TryGetDateTimeOffset(out var instant))
            throw new JsonException("An explicit UTC offset is required.");
        return instant;
    }
    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) => writer.WriteStringValue(value);
}
