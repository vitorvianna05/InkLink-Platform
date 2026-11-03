using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using InkLink.Engagements.Application.Negotiations;
using InkLink.Scheduling.Application.Reservations;

namespace InkLink.Scheduling.IntegrationTests;

internal static class Scenario
{
    private static int days;
    public static DateOnly Day() => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30 + Interlocked.Increment(ref days));
    public static HttpClient Client(SchedulingFixture fixture, string user)
    {
        var client = fixture.Host.CreateClient(); client.DefaultRequestHeaders.Authorization = new("Bearer", fixture.Base.Token(user)); return client;
    }
    public static async Task<EngagementView> EngagementAsync(SchedulingFixture fixture, bool accept = true)
    {
        using var catalog = fixture.Base.Catalog.CreateClient(); catalog.DefaultRequestHeaders.Authorization = new("Bearer", fixture.Base.Token("bob"));
        var published = await catalog.PutAsJsonAsync("/api/v1/artists/me", new { name = "Bob Artist", studio = "Studio", city = "São Paulo", biography = "Portfolio", styles = new[] { "Blackwork" }, startingPrice = 300 });
        Assert.True(published.IsSuccessStatusCode);
        var artist = (await published.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<Guid>();
        using var alice = fixture.Base.Engagements.CreateClient(); alice.DefaultRequestHeaders.Authorization = new("Bearer", fixture.Base.Token("alice"));
        using var bob = fixture.Base.Engagements.CreateClient(); bob.DefaultRequestHeaders.Authorization = new("Bearer", fixture.Base.Token("bob"));
        var request = await PostAsync(alice, "/api/v2/engagements", new RequestInput(artist, "Projeto de teste", "Braço", 10), Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Created, request.StatusCode);
        var engagement = (await request.Content.ReadFromJsonAsync<EngagementView>())!;
        if (!accept) return engagement;
        var response = await PostAsync(bob, $"/api/v2/engagements/{engagement.Id}/proposals", new ProposalInput("Condições", 450), Guid.NewGuid());
        var proposal = (await response.Content.ReadFromJsonAsync<ProposalView>())!;
        Assert.Equal(HttpStatusCode.OK, (await alice.PutAsync($"/api/v2/engagements/{engagement.Id}/proposals/{proposal.Id}/acceptance", null)).StatusCode);
        return engagement;
    }
    public static Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object value, Guid key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(value) };
        request.Headers.Add("Idempotency-Key", key.ToString()); return client.SendAsync(request);
    }
    public static DateTimeOffset At(DateOnly date, int hour, int minute = 0) => new(date.Year, date.Month, date.Day, hour, minute, 0, TimeSpan.FromHours(-3));
    public static async Task AvailableAsync(HttpClient artist, DateOnly date) =>
        Assert.Equal(HttpStatusCode.OK, (await artist.PutAsJsonAsync($"/api/v1/scheduling/availability/{date:yyyy-MM-dd}",
            new { windows = new[] { new { start = "08:00:00", end = "18:00:00" } } })).StatusCode);
    public static Task<HttpResponseMessage> ReserveAsync(HttpClient artist, Guid engagement, DateOnly date, int start = 10, int end = 11, Guid? operation = null) =>
        PostAsync(artist, "/api/v1/scheduling/reservations", new ReserveInput(engagement, At(date, start), At(date, end)), operation ?? Guid.NewGuid());
}
