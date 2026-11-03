using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace InkLink.Catalog.Application.Queries;

public sealed record PagePosition(double Rank, string Name, Guid Id);
internal sealed record CursorEnvelope(string Resource, string Criteria, PagePosition Position);

public static class CatalogCursor
{
    public static string Fingerprint(params string[] values) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(values))));

    public static PagePosition? Decode(string? cursor, string resource, string criteria)
    {
        if (cursor is null) return null;
        if (cursor.Length > 2048) throw CatalogFailure.Invalid("cursor", "Cursor is invalid.");
        try
        {
            var decoded = JsonSerializer.Deserialize<CursorEnvelope>(Convert.FromBase64String(cursor));
            if (decoded is null || decoded.Resource != resource || decoded.Criteria != criteria || decoded.Position is null
                || decoded.Position.Id == Guid.Empty || !double.IsFinite(decoded.Position.Rank) || decoded.Position.Rank < 0
                || decoded.Position.Name is null || decoded.Position.Name.Length > 200)
                throw CatalogFailure.Invalid("cursor", "Cursor does not match this resource and its criteria.");
            return decoded.Position;
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            throw CatalogFailure.Invalid("cursor", "Cursor is invalid.");
        }
    }

    public static string Encode(string resource, string criteria, PagePosition position) =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new CursorEnvelope(resource, criteria, position)));
}
