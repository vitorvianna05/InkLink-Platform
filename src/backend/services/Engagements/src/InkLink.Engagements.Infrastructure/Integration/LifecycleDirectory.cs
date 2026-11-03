using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using InkLink.Engagements.Application.Lifecycle;
using InkLink.Engagements.Domain;
namespace InkLink.Engagements.Infrastructure.Integration;

public sealed record LifecycleAddresses(Uri Scheduling, Uri Messaging);
public sealed class LifecycleDirectory(HttpClient http, ServiceTokenClient tokens, CatalogCredentials credentials, LifecycleAddresses addresses) : ILifecycleDirectory
{
    public async Task<bool> FreezeAsync(Engagement e, CancellationToken ct)
    {
        using var response = await SendAsync(new Uri(addresses.Scheduling, $"api/v1/internal/engagements/{e.Id}/freeze"),
            "scheduling.engagement.freeze", new { e.ClientId, e.ArtistAccountId }, ct);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var problem = await response.Content.ReadFromJsonAsync<Problem>(ct);
            if (problem?.Code == "scheduling.future_reservations") return false;
        }
        response.EnsureSuccessStatusCode(); return true;
    }
    public async Task ApplyConversationAsync(Engagement e, bool close, CancellationToken ct)
    {
        using var response = await SendAsync(new Uri(addresses.Messaging, $"api/v1/internal/engagements/{e.Id}/lifecycle"),
            "messaging.conversation.lifecycle", new { e.ClientId, e.ArtistAccountId, Close = close }, ct);
        response.EnsureSuccessStatusCode();
    }
    private async Task<HttpResponseMessage> SendAsync(Uri address, string scope, object input, CancellationToken ct)
    {
        using var tokenResponse = await tokens.Http.PostAsync(credentials.TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
        { ["grant_type"] = "client_credentials", ["client_id"] = credentials.ClientId, ["client_secret"] = credentials.ClientSecret, ["scope"] = scope }), ct);
        tokenResponse.EnsureSuccessStatusCode();
        var token = await tokenResponse.Content.ReadFromJsonAsync<Token>(ct);
        if (string.IsNullOrWhiteSpace(token?.AccessToken)) throw new HttpRequestException("Missing service credential.");
        using var request = new HttpRequestMessage(HttpMethod.Put, address) { Content = JsonContent.Create(input) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return await http.SendAsync(request, ct);
    }
    private sealed record Token([property: JsonPropertyName("access_token")] string AccessToken);
    private sealed record Problem(string Code);
}
