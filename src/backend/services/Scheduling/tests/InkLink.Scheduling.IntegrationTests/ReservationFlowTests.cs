using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using InkLink.Engagements.Application.Scheduling;
using InkLink.Engagements.Infrastructure.Persistence;
using InkLink.Identity.IntegrationTests;
using InkLink.Scheduling.Application.Reservations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Forwarder;

namespace InkLink.Scheduling.IntegrationTests;

[Collection("scheduling")]
public sealed class ReservationFlowTests
{
    [Fact]
    public async Task Real_identity_outage_recovery_and_duplicate_events_preserve_reservation_and_receipt()
    {
        var provider = new SecurityFixture(); var fixture = new SchedulingFixture(); await using var broker = new BrokerHarness();
        try
        {
            await provider.InitializeAsync(); fixture.Base.UseRealIdentity(provider); await fixture.InitializeAsync();
            await broker.InitializeAsync(); broker.VerifyRolePermissions(); await broker.StopAsync();
            var engagement = await Scenario.EngagementAsync(fixture); var day = Scenario.Day();
            using var publisher = fixture.Host.WithWebHostBuilder(b => b.UseSetting("Broker:PublisherUri", broker.PublisherUri).UseSetting("Broker:EnableWorkers", "true"));
            using var bob = publisher.CreateClient(); bob.DefaultRequestHeaders.Authorization = new("Bearer", fixture.Base.Token("bob"));
            await Scenario.AvailableAsync(bob, day);
            var key = Guid.NewGuid(); var response = await Scenario.ReserveAsync(bob, engagement.Id, day, operation: key); Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var reservation = (await response.Content.ReadFromJsonAsync<ReservationView>())!;
            string payload; Guid eventId;
            await using (var db = fixture.CreateOwnerContext())
            {
                var entry = await db.Outbox.SingleAsync(o => o.ReservationId == reservation.Id); Assert.Null(entry.PublishedAt); payload = entry.Payload; eventId = entry.Id;
            }
            using var consumer = fixture.Base.NewHost().WithWebHostBuilder(b => b.UseSetting("Broker:ReservationConsumerUri", broker.ConsumerUri).UseSetting("Broker:EnableReservationConsumer", "true"));
            using var care = consumer.CreateClient(); care.DefaultRequestHeaders.Authorization = new("Bearer", fixture.Base.Token("bob"));
            await broker.StartAsync();
            await BrokerHarness.UntilAsync(async () => { await using var db = fixture.Base.CreateOwnerContext(); return await db.ReservationReceipts.AnyAsync(r => r.Id == reservation.Id); }, "Reservation receipt was not recovered.");
            await BrokerHarness.UntilAsync(async () => { await using var db = fixture.CreateOwnerContext(); return (await db.Outbox.SingleAsync(o => o.Id == eventId)).PublishedAt is not null; }, "Outbox confirmation missing.");
            broker.Publish(Encoding.UTF8.GetBytes(payload));
            var duplicate = JsonNode.Parse(payload)!; var duplicateId = Guid.NewGuid(); duplicate["messageId"] = duplicateId;
            broker.Publish(Encoding.UTF8.GetBytes(duplicate.ToJsonString()));
            await BrokerHarness.UntilAsync(async () => { await using var db = fixture.Base.CreateOwnerContext(); return await db.ReservationInbox.AnyAsync(r => r.Id == duplicateId); }, "Duplicate was not consumed.");
            await using (var db = fixture.Base.CreateOwnerContext())
            {
                Assert.Equal(1, await db.ReservationReceipts.CountAsync(r => r.Id == reservation.Id));
                Assert.Equal("Agreed", (await db.Engagements.SingleAsync(e => e.Id == engagement.Id)).State.ToString());
            }
            // A receipt is not full lifecycle coordination and does not create another engagement.
            using var alice = Scenario.Client(fixture, "alice");
            Assert.Equal(reservation, await alice.GetFromJsonAsync<ReservationView>($"/api/v1/scheduling/reservations/{reservation.Id}"));
            var path = $"/api/v1/internal/engagements/{engagement.Id}/scheduling-context";
            Assert.Equal(HttpStatusCode.Forbidden, (await care.GetAsync(path)).StatusCode);
            using var restarted = new SchedulingHost(fixture); using var reopened = restarted.CreateClient(); reopened.DefaultRequestHeaders.Authorization = new("Bearer", fixture.Base.Token("bob"));
            Assert.Equal(reservation, await reopened.GetFromJsonAsync<ReservationView>($"/api/v1/scheduling/operations/{key}"));
            var incompatible = JsonNode.Parse(payload)!; var badId = Guid.NewGuid(); incompatible["messageId"] = badId; incompatible["data"]!["clientId"] = Guid.NewGuid();
            broker.Publish(Encoding.UTF8.GetBytes(incompatible.ToJsonString()));
            using var admin = broker.Admin.CreateConnection(); using var channel = admin.CreateModel();
            await BrokerHarness.UntilAsync(() => Task.FromResult(channel.MessageCount("inklink.engagements.reservations.invalid.v1") > 0), "Incompatible receipt was not quarantined.");
            await using (var db = fixture.Base.CreateOwnerContext()) Assert.False(await db.ReservationInbox.AnyAsync(i => i.Id == badId));
            using var gateway = fixture.Base.Gateway().WithWebHostBuilder(b =>
            {
                b.UseSetting("Gateway:SchedulingAddress", "http://scheduling.test/");
                b.ConfigureTestServices(services => services.AddSingleton<IForwarderHttpClientFactory>(new SchedulingProxy(publisher.Server.CreateHandler())));
            });
            using var external = gateway.CreateClient(); external.DefaultRequestHeaders.Authorization = new("Bearer", fixture.Base.Token("bob"));
            Assert.Equal(HttpStatusCode.OK, (await external.GetAsync($"/api/v1/scheduling/reservations/{reservation.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await external.GetAsync(path)).StatusCode);
        }
        finally { await fixture.DisposeAsync(); await provider.DisposeAsync(); }
    }
}
internal sealed class SchedulingProxy(HttpMessageHandler handler) : IForwarderHttpClientFactory
{
    public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) => new(handler, false);
}
