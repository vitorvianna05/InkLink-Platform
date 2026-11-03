using InkLink.Scheduling.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;

namespace InkLink.Scheduling.IntegrationTests;

[Collection("scheduling")]
public sealed class OutboxRecoveryTests(SchedulingFixture fixture)
{
    [Fact]
    public async Task Invalid_trace_metadata_is_quarantined_without_blocking_the_next_event()
    {
        var bad = new OutboxEvent { Id = Guid.NewGuid(), ReservationId = Guid.NewGuid(), Payload = "{\"traceParent\":123}", CreatedAt = DateTimeOffset.UtcNow.AddYears(-1), Attempts = 9 };
        var next = new OutboxEvent { Id = Guid.NewGuid(), ReservationId = Guid.NewGuid(), Payload = "{\"traceParent\":null}", CreatedAt = bad.CreatedAt.AddMinutes(1) };
        await using (var db = fixture.CreateOwnerContext()) { db.Outbox.AddRange(bad, next); await db.SaveChangesAsync(); await db.Entry(bad).ReloadAsync(); }
        using (var host = fixture.Host.WithWebHostBuilder(b => b.UseSetting("Broker:EnableWorkers", "true").UseSetting("Broker:PublisherUri", "invalid-uri")))
        {
            using var client = host.CreateClient();
            await BrokerHarness.UntilAsync(async () =>
            {
                await using var db = fixture.CreateOwnerContext();
                return (await db.Outbox.SingleAsync(o => o.Id == bad.Id)).QuarantinedAt is not null
                    && (await db.Outbox.SingleAsync(o => o.Id == next.Id)).Attempts > 0;
            }, "Invalid telemetry bypassed the retry budget or blocked the next event.");
        }
        await using (var db = fixture.CreateOwnerContext())
        {
            var result = await db.Outbox.SingleAsync(o => o.Id == bad.Id);
            Assert.Equal(10, result.Attempts); Assert.Null(result.PublishedAt);
            Assert.Equal(bad.Payload, result.Payload); Assert.NotNull(result.LastError);
            var later = await db.Outbox.SingleAsync(o => o.Id == next.Id);
            Assert.Null(later.PublishedAt); Assert.NotNull(later.NextAttemptAt);
            db.Outbox.RemoveRange(result, later); await db.SaveChangesAsync();
        }
    }
}
