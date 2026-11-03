using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using InkLink.Scheduling.Application;
using InkLink.Scheduling.Application.Reservations;

namespace InkLink.Scheduling.Infrastructure.Integration;

public sealed record SchedulingCredentials(Uri TokenEndpoint, string Secret);
public sealed class SchedulingTokenClient(HttpClient http)
{
    public HttpClient Http { get; } = http;
}
public sealed class TechnicalTokens(SchedulingTokenClient tokens, SchedulingCredentials credentials)
{
    public async Task<string> GetAsync(string scope, CancellationToken ct)
    {
        using var response = await tokens.Http.PostAsync(credentials.TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "inklink-scheduling",
            ["client_secret"] = credentials.Secret,
            ["scope"] = scope
        }), ct);
        if (!response.IsSuccessStatusCode) throw Unavailable();
        var token = await response.Content.ReadFromJsonAsync<Token>(ct);
        return !string.IsNullOrWhiteSpace(token?.AccessToken) ? token.AccessToken : throw Unavailable();
    }
    public static SchedulingFailure Unavailable() => new(SchedulingError.Unavailable, "scheduling.context_unavailable");
    private sealed record Token([property: JsonPropertyName("access_token")] string AccessToken);
}
public sealed class EngagementDirectory(HttpClient http, TechnicalTokens tokens) : IEngagementDirectory
{
    public async Task<SchedulingContext> ReadAsync(Guid id, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"api/v1/internal/engagements/{id}/scheduling-context");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetAsync("engagements.scheduling-context.read", ct));
            using var response = await http.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.NotFound) throw ManageSchedule.NotFound();
            if (!response.IsSuccessStatusCode) throw TechnicalTokens.Unavailable();
            var result = await response.Content.ReadFromJsonAsync<SchedulingContext>(ct);
            if (result is null || result.Id != id || result.ClientId == Guid.Empty || result.ArtistAccountId == Guid.Empty || result.ClientId == result.ArtistAccountId
                || result.Revision <= 0 || string.IsNullOrWhiteSpace(result.Project) || string.IsNullOrWhiteSpace(result.ArtistName)) throw TechnicalTokens.Unavailable();
            return result;
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        { throw TechnicalTokens.Unavailable(); }
    }
}
public sealed class ParticipantDirectory(HttpClient http, TechnicalTokens tokens) : IParticipantDirectory
{
    public async Task<string> NameAsync(Guid id, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"api/v1/internal/profiles/{id}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetAsync("identity.profile.read", ct));
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) throw TechnicalTokens.Unavailable();
            var result = await response.Content.ReadFromJsonAsync<Profile>(ct);
            return result is not null && result.Id == id && !string.IsNullOrWhiteSpace(result.DisplayName) ? result.DisplayName : throw TechnicalTokens.Unavailable();
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        { throw TechnicalTokens.Unavailable(); }
    }
    private sealed record Profile(Guid Id, string DisplayName);
}
