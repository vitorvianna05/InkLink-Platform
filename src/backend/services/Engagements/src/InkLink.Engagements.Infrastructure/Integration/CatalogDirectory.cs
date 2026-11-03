using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using InkLink.Engagements.Application;
using InkLink.Engagements.Application.Negotiations;

namespace InkLink.Engagements.Infrastructure.Integration;

public sealed record CatalogCredentials(Uri TokenEndpoint, string ClientId, string ClientSecret);

public sealed class ServiceTokenClient(HttpClient http)
{
    public HttpClient Http { get; } = http;
}

public sealed class CatalogDirectory(HttpClient http, ServiceTokenClient tokenClient, CatalogCredentials credentials) : ICatalogDirectory
{
    public async Task<CatalogContext> GetAsync(Guid artistId, Guid? flashId, CancellationToken cancellationToken)
    {
        try
        {
            using var tokenResponse = await tokenClient.Http.PostAsync(credentials.TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = credentials.ClientId,
                ["client_secret"] = credentials.ClientSecret,
                ["scope"] = "catalog.engagement-context.read"
            }), cancellationToken);
            if (!tokenResponse.IsSuccessStatusCode) throw Unavailable();
            var token = await tokenResponse.Content.ReadFromJsonAsync<ServiceToken>(cancellationToken);
            if (string.IsNullOrWhiteSpace(token?.AccessToken)) throw Unavailable();
            var path = $"api/v1/internal/artists/{artistId}/engagement-context" + (flashId.HasValue ? $"?flashId={flashId}" : string.Empty);
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
            using var response = await http.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound) throw new EngagementsFailure(EngagementsError.NotFound, "engagements.catalog_context_not_found");
            if (!response.IsSuccessStatusCode) throw Unavailable();
            var result = await response.Content.ReadFromJsonAsync<CatalogContext>(cancellationToken);
            if (result is null || result.ArtistId != artistId || result.ArtistAccountId == Guid.Empty || result.Flash?.Id != flashId
                || string.IsNullOrWhiteSpace(result.Name) || string.IsNullOrWhiteSpace(result.Studio) || string.IsNullOrWhiteSpace(result.City)) throw Unavailable();
            return result;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw Unavailable();
        }
    }

    private static EngagementsFailure Unavailable() => new(EngagementsError.Unavailable, "engagements.catalog_unavailable");
    private sealed record ServiceToken([property: JsonPropertyName("access_token")] string AccessToken);
}
