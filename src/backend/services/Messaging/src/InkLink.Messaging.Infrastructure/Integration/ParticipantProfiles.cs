using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace InkLink.Messaging.Infrastructure.Integration;

public sealed record ParticipantCredentials(Uri TokenEndpoint, string ClientSecret);
public sealed class MessagingTokenClient(HttpClient http) { public HttpClient Http { get; } = http; }

// A request-scoped directory caches only within this presentation operation, never across users/requests.
public sealed class ParticipantProfiles(HttpClient identity, MessagingTokenClient tokens, ParticipantCredentials credentials)
{
    private readonly ConcurrentDictionary<string, Lazy<Task<ParticipantProfile>>> values = new();
    private readonly ConcurrentDictionary<string, Lazy<Task<string>>> token = new();

    /// <summary>Nome e foto do participante (include=photo); a foto privada chega assinada e com validade curta.</summary>
    public Task<ParticipantProfile> ProfileAsync(Guid accountId, CancellationToken ct) =>
        values.GetOrAdd(accountId.ToString(), _ => new Lazy<Task<ParticipantProfile>>(() => ReadAsync(accountId, ct))).Value;

    public async Task<string> NameAsync(Guid accountId, CancellationToken ct) => (await ProfileAsync(accountId, ct)).DisplayName;

    private async Task<ParticipantProfile> ReadAsync(Guid accountId, CancellationToken ct)
    {
        try
        {
            var token = await this.token.GetOrAdd("token", _ => new Lazy<Task<string>>(() => TokenAsync(ct))).Value;
            using var request = new HttpRequestMessage(HttpMethod.Get, $"api/v1/internal/profiles/{accountId}?include=photo");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await identity.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) throw ConversationPresentationDirectory.Unavailable();
            var profile = await response.Content.ReadFromJsonAsync<Profile>(ct);
            if (profile is null || profile.Id != accountId || string.IsNullOrWhiteSpace(profile.DisplayName)) throw ConversationPresentationDirectory.Unavailable();
            return new(profile.DisplayName, Https(profile.PhotoUrl), Https(profile.PhotoThumbnailUrl));
        }
        catch (Exception exception) when (exception is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        { throw ConversationPresentationDirectory.Unavailable(); }
    }

    private async Task<string> TokenAsync(CancellationToken ct)
    {
        using var response = await tokens.Http.PostAsync(credentials.TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "inklink-messaging",
            ["client_secret"] = credentials.ClientSecret,
            ["scope"] = "identity.profile.read"
        }), ct);
        if (!response.IsSuccessStatusCode) throw ConversationPresentationDirectory.Unavailable();
        var token = await response.Content.ReadFromJsonAsync<Token>(ct);
        return !string.IsNullOrWhiteSpace(token?.AccessToken) ? token.AccessToken : throw ConversationPresentationDirectory.Unavailable();
    }
    private sealed record Token([property: JsonPropertyName("access_token")] string AccessToken);
    private sealed record Profile(Guid Id, string DisplayName, string? PhotoUrl, string? PhotoThumbnailUrl);
    private static string? Https(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? value : null;
}

public sealed record ParticipantProfile(string DisplayName, string? PhotoUrl, string? PhotoThumbnailUrl);
