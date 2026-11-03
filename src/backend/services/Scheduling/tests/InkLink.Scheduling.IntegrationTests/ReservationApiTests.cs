using System.Net;
using System.Net.Http.Json;
using System.Text;
using InkLink.Scheduling.Application.Reservations;
using InkLink.Scheduling.Domain;
using InkLink.Scheduling.Infrastructure.Integration;
using InkLink.Scheduling.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InkLink.Scheduling.IntegrationTests;

[Collection("scheduling")]
public sealed class ReservationApiTests(SchedulingFixture fixture)
{
    [Fact]
    public async Task Parties_share_reservation_calendar_and_persistent_operation_without_third_party_access()
    {
        var engagement = await Scenario.EngagementAsync(fixture); var day = Scenario.Day();
        using var bob = Scenario.Client(fixture, "bob"); using var alice = Scenario.Client(fixture, "alice"); using var carol = Scenario.Client(fixture, "carol");
        await Scenario.AvailableAsync(bob, day);
        var availability = await alice.GetFromJsonAsync<AvailabilityView>($"/api/v1/scheduling/engagements/{engagement.Id}/availability/{day:yyyy-MM-dd}");
        Assert.False(availability!.GuaranteesReservation); Assert.Equal("America/Sao_Paulo", availability.TimeZoneId);
        Assert.Equal(HttpStatusCode.Forbidden, (await Scenario.ReserveAsync(carol, engagement.Id, day)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Scenario.ReserveAsync(alice, engagement.Id, day)).StatusCode);
        var key = Guid.NewGuid(); var response = await Scenario.ReserveAsync(bob, engagement.Id, day, operation: key);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var reservation = (await response.Content.ReadFromJsonAsync<ReservationView>())!;
        Assert.Equal("alice", reservation.ClientName); Assert.Equal("Bob Artist", reservation.ArtistName);
        Assert.Equal("Confirmed", reservation.State); Assert.Equal("BRL", reservation.Currency);
        var clientView = await alice.GetFromJsonAsync<ReservationView>($"/api/v1/scheduling/reservations/{reservation.Id}");
        Assert.Equal(reservation, clientView);
        Assert.Equal(HttpStatusCode.NotFound, (await carol.GetAsync($"/api/v1/scheduling/reservations/{reservation.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await carol.GetAsync($"/api/v1/scheduling/engagements/{engagement.Id}/availability/{day:yyyy-MM-dd}")).StatusCode);
        var end = day.AddDays(1);
        var artistPage = await bob.GetFromJsonAsync<Page<ReservationView>>($"/api/v1/scheduling/calendar?from={day:yyyy-MM-dd}&to={end:yyyy-MM-dd}");
        var clientPage = await alice.GetFromJsonAsync<Page<ReservationView>>($"/api/v1/scheduling/calendar?perspective=client&from={day:yyyy-MM-dd}&to={end:yyyy-MM-dd}");
        Assert.Equal(artistPage!.Items, clientPage!.Items);
        using var restarted = new SchedulingHost(fixture); using var reopened = restarted.CreateClient(); reopened.DefaultRequestHeaders.Authorization = new("Bearer", fixture.Base.Token("bob"));
        Assert.Equal(reservation, await reopened.GetFromJsonAsync<ReservationView>($"/api/v1/scheduling/operations/{key}"));
    }
    [Fact]
    public async Task Adjacent_sessions_are_allowed_and_all_overlap_shapes_are_rejected()
    {
        var e = await Scenario.EngagementAsync(fixture); var day = Scenario.Day(); using var bob = Scenario.Client(fixture, "bob"); await Scenario.AvailableAsync(bob, day);
        Assert.Equal(HttpStatusCode.Created, (await Scenario.ReserveAsync(bob, e.Id, day)).StatusCode);
        foreach (var interval in new[] { (9, 12), (10, 11) })
            Assert.Equal(HttpStatusCode.Conflict, (await Scenario.ReserveAsync(bob, e.Id, day, interval.Item1, interval.Item2)).StatusCode);
        foreach (var pair in new[] { (9, 30, 10, 30), (10, 15, 10, 45) })
        {
            var input = new ReserveInput(e.Id, Scenario.At(day, pair.Item1, pair.Item2), Scenario.At(day, pair.Item3, pair.Item4));
            Assert.Equal(HttpStatusCode.Conflict, (await Scenario.PostAsync(bob, "/api/v1/scheduling/reservations", input, Guid.NewGuid())).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Created, (await Scenario.ReserveAsync(bob, e.Id, day, 9, 10)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Scenario.ReserveAsync(bob, e.Id, day, 11, 12)).StatusCode);
        var page = await bob.GetFromJsonAsync<Page<ReservationView>>($"/api/v1/scheduling/calendar?from={day:yyyy-MM-dd}&to={day.AddDays(1):yyyy-MM-dd}&limit=1");
        Assert.Equal(9, TimeZoneInfo.ConvertTime(page!.Items[0].Start, ArtistCalendar.Zone("America/Sao_Paulo")).Hour);
        var cursor = Uri.EscapeDataString(page.NextCursor!);
        using var alice = Scenario.Client(fixture, "alice");
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.GetAsync($"/api/v1/scheduling/calendar?perspective=client&from={day:yyyy-MM-dd}&to={day.AddDays(1):yyyy-MM-dd}&cursor={cursor}")).StatusCode);
    }
    [Fact]
    public async Task Concurrent_conflicts_and_repeated_operations_are_serialized()
    {
        var e = await Scenario.EngagementAsync(fixture); var day = Scenario.Day(); using var bob = Scenario.Client(fixture, "bob"); await Scenario.AvailableAsync(bob, day);
        var conflicts = await Task.WhenAll(Scenario.ReserveAsync(bob, e.Id, day), Scenario.ReserveAsync(bob, e.Id, day));
        Assert.Single(conflicts, r => r.StatusCode == HttpStatusCode.Created); Assert.Single(conflicts, r => r.StatusCode == HttpStatusCode.Conflict);
        var key = Guid.NewGuid();
        var repeated = await Task.WhenAll(Scenario.ReserveAsync(bob, e.Id, day, 12, 13, key), Scenario.ReserveAsync(bob, e.Id, day, 12, 13, key));
        Assert.Single(repeated, r => r.StatusCode == HttpStatusCode.Created); Assert.Single(repeated, r => r.StatusCode == HttpStatusCode.OK);
        var a = await repeated[0].Content.ReadFromJsonAsync<ReservationView>(); var b = await repeated[1].Content.ReadFromJsonAsync<ReservationView>(); Assert.Equal(a, b);
        Assert.Equal(HttpStatusCode.Conflict, (await Scenario.ReserveAsync(bob, e.Id, day, 14, 15, key)).StatusCode);
        using var outage = fixture.Host.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
            services.AddHttpClient<IEngagementDirectory, EngagementDirectory>().ConfigurePrimaryHttpMessageHandler(() => new Unavailable())));
        using var client = outage.CreateClient(); client.DefaultRequestHeaders.Authorization = new("Bearer", fixture.Base.Token("bob"));
        Assert.Equal(HttpStatusCode.OK, (await Scenario.ReserveAsync(client, e.Id, day, 12, 13, key)).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Scenario.ReserveAsync(client, e.Id, day, 14, 15)).StatusCode);
        await using var db = fixture.CreateOwnerContext(); Assert.Equal(2, await db.Reservations.CountAsync(r => r.LocalDate == day));
        Assert.Equal(2, await db.Outbox.CountAsync(o => db.Reservations.Any(r => r.LocalDate == day && r.Id == o.ReservationId)));
    }
    [Fact]
    public async Task Availability_cannot_be_reduced_under_existing_reservations_and_race_remains_consistent()
    {
        var e = await Scenario.EngagementAsync(fixture); var day = Scenario.Day(); using var bob = Scenario.Client(fixture, "bob"); await Scenario.AvailableAsync(bob, day);
        var reserve = Scenario.ReserveAsync(bob, e.Id, day);
        var reduce = bob.PutAsJsonAsync($"/api/v1/scheduling/availability/{day:yyyy-MM-dd}", new { windows = Array.Empty<object>() });
        var responses = await Task.WhenAll(reserve, reduce);
        Assert.False(responses[0].StatusCode == HttpStatusCode.Created && responses[1].StatusCode == HttpStatusCode.OK);
        if (responses[0].StatusCode != HttpStatusCode.Created) { await Scenario.AvailableAsync(bob, day); Assert.Equal(HttpStatusCode.Created, (await Scenario.ReserveAsync(bob, e.Id, day)).StatusCode); }
        Assert.Equal(HttpStatusCode.Conflict, (await bob.PutAsJsonAsync($"/api/v1/scheduling/availability/{day:yyyy-MM-dd}", new { windows = Array.Empty<object>() })).StatusCode);
    }
    [Fact]
    public async Task Invalid_intervals_unaccepted_proposals_and_offsetless_json_do_not_write_reservations()
    {
        var e = await Scenario.EngagementAsync(fixture); var pending = await Scenario.EngagementAsync(fixture, false); var day = Scenario.Day(); using var bob = Scenario.Client(fixture, "bob");
        await Scenario.AvailableAsync(bob, day);
        Assert.Equal(HttpStatusCode.Conflict, (await Scenario.ReserveAsync(bob, pending.Id, day)).StatusCode);
        foreach (var interval in new[] { (7, 8), (12, 11), (23, 24) })
        {
            var start = Scenario.At(day, interval.Item1); var end = interval.Item2 == 24 ? Scenario.At(day.AddDays(1), 1) : Scenario.At(day, interval.Item2);
            Assert.Equal(HttpStatusCode.BadRequest, (await Scenario.PostAsync(bob, "/api/v1/scheduling/reservations", new ReserveInput(e.Id, start, end), Guid.NewGuid())).StatusCode);
        }
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/scheduling/reservations")
        { Content = new StringContent($"{{\"engagementId\":\"{e.Id}\",\"start\":\"{day:yyyy-MM-dd}T10:00:00\",\"end\":\"{day:yyyy-MM-dd}T11:00:00\"}}", Encoding.UTF8, "application/json") };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString()); Assert.Equal(HttpStatusCode.BadRequest, (await bob.SendAsync(request)).StatusCode);
        await using var db = fixture.CreateOwnerContext(); Assert.False(await db.Reservations.AnyAsync(r => r.LocalDate == day));
        Assert.False(await db.Outbox.AnyAsync(o => db.Reservations.Any(r => r.Id == o.ReservationId && r.LocalDate == day)));
    }
    [Fact]
    public async Task Technical_context_requires_the_exact_service_scope_and_empty_calendar_is_explicit()
    {
        var engagement = await Scenario.EngagementAsync(fixture);
        using var client = fixture.Base.Engagements.CreateClient();
        var path = $"/api/v1/internal/engagements/{engagement.Id}/scheduling-context";
        foreach (var token in new[]
        {
            fixture.Base.Token("bob"),
            fixture.Base.Token("service","service","inklink-messaging","engagements.scheduling-context.read"),
            fixture.Base.Token("service","service","inklink-scheduling","")
        })
        {
            client.DefaultRequestHeaders.Authorization = new("Bearer", token);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
        }
        client.DefaultRequestHeaders.Authorization = new("Bearer", fixture.Base.Token("service", "service", "inklink-scheduling", "engagements.scheduling-context.read"));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);
        using var carol = Scenario.Client(fixture, "carol"); var day = Scenario.Day();
        var page = await carol.GetFromJsonAsync<Page<ReservationView>>($"/api/v1/scheduling/calendar?perspective=client&from={day:yyyy-MM-dd}&to={day.AddDays(1):yyyy-MM-dd}");
        Assert.Empty(page!.Items); Assert.Equal(0, page.Total);
        Assert.Equal(HttpStatusCode.BadRequest, (await carol.GetAsync($"/api/v1/scheduling/calendar?perspective=client&from={day:yyyy-MM-dd}&to={day.AddDays(94):yyyy-MM-dd}")).StatusCode);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("true")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("\"01/15/2030 10:00:00-03:00\"")]
    public async Task Invalid_timestamp_tokens_return_validation_problem_without_writes(string token)
    {
        using var bob = Scenario.Client(fixture, "bob"); var operation = Guid.NewGuid();
        var json = $"{{\"engagementId\":\"{Guid.NewGuid()}\",\"start\":{token},\"end\":\"2030-01-15T11:00:00-03:00\"}}";
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/scheduling/reservations") { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        request.Headers.Add("Idempotency-Key", operation.ToString());
        var response = await bob.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        await using var db = fixture.CreateOwnerContext();
        Assert.False(await db.Reservations.AnyAsync(r => r.OperationId == operation));
    }

    private sealed class Unavailable : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }
}
