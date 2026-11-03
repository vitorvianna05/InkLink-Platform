using System.Text.Json.Nodes;

namespace InkLink.Gateway.Documentation;

public sealed record GatewayDocument(string Name, Uri Address, string[] Prefixes);
public sealed record GatewayDocumentationOptions(string Authority, IReadOnlyList<GatewayDocument> Documents);

public sealed class GatewayDocuments(HttpClient http, GatewayDocumentationOptions options)
{
    public bool Contains(string name) => options.Documents.Any(d => d.Name == name);
    public async Task<JsonObject> ReadAsync(string name, string gatewayBase, CancellationToken ct)
    {
        var source = options.Documents.Single(d => d.Name == name);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(source.Address, "openapi/v1.json"));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await response.Content.LoadIntoBufferAsync(4 * 1024 * 1024, ct);
        var document = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct)) as JsonObject
            ?? throw new InvalidDataException("Invalid OpenAPI document.");
        var paths = document["paths"] as JsonObject ?? throw new InvalidDataException("OpenAPI paths missing.");
        foreach (var path in paths.Select(p => p.Key).ToArray())
        {
            if (!source.Prefixes.Any(prefix => path == prefix || path.StartsWith(prefix + "/", StringComparison.Ordinal)))
            { paths.Remove(path); continue; }
            if (paths[path] is not JsonObject item) throw new InvalidDataException("Invalid path.");
            item.Remove("servers");
            foreach (var operation in item.Select(p => p.Value).OfType<JsonObject>())
            {
                operation.Remove("servers");
                operation.Remove("callbacks");
            }
        }
        document.Remove("webhooks");
        document["servers"] = new JsonArray(new JsonObject { ["url"] = gatewayBase });
        var components = document["components"] as JsonObject ?? new JsonObject();
        document["components"] = components.Parent is null ? components : components.DeepClone();
        components = (JsonObject)document["components"]!;
        components["securitySchemes"] = new JsonObject
        {
            ["Bearer"] = new JsonObject
            {
                ["type"] = "oauth2",
                ["description"] = "Keycloak: Authorization Code com PKCE. Use uma conta Client/Artist; sem segredo de cliente.",
                ["flows"] = new JsonObject
                {
                    ["authorizationCode"] = new JsonObject
                    {
                        ["authorizationUrl"] = options.Authority + "/protocol/openid-connect/auth",
                        ["tokenUrl"] = options.Authority + "/protocol/openid-connect/token",
                        ["scopes"] = new JsonObject { ["openid"] = "Identificação", ["profile"] = "Perfil" }
                    }
                }
            }
        };
        PruneSchemas(document, paths, components);
        return document;
    }
    private static void PruneSchemas(JsonObject document, JsonObject paths, JsonObject components)
    {
        // Keep only components reachable from public operations; internal DTOs are not republished.
        var needed = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>();
        void Visit(JsonNode? node)
        {
            if (node is JsonObject obj)
                foreach (var pair in obj)
                {
                    if (pair.Key == "$ref" && pair.Value is JsonValue value && value.TryGetValue<string>(out var reference)
                        && reference.StartsWith("#/components/", StringComparison.Ordinal) && needed.Add(reference)) pending.Enqueue(reference);
                    else Visit(pair.Value);
                }
            else if (node is JsonArray array) foreach (var item in array) Visit(item);
        }
        Visit(paths);
        while (pending.TryDequeue(out var reference))
        {
            var segments = reference.Split('/');
            if (segments.Length != 4) throw new InvalidDataException("Unsupported component reference.");
            var key = segments[3].Replace("~1", "/").Replace("~0", "~");
            Visit(components[segments[2]]?[key]);
        }
        foreach (var category in components.ToArray())
        {
            if (category.Key == "securitySchemes") continue;
            if (category.Value is not JsonObject entries) { components.Remove(category.Key); continue; }
            foreach (var entry in entries.ToArray())
            {
                var reference = "#/components/" + category.Key + "/" + entry.Key.Replace("~", "~0").Replace("/", "~1");
                if (!needed.Contains(reference)) entries.Remove(entry.Key);
            }
        }
    }
}
