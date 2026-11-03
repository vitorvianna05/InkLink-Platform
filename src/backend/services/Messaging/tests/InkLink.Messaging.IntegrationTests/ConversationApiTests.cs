using System.Net;
using System.Net.Http.Json;
using InkLink.Messaging.Application.Conversations;
using InkLink.Messaging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace InkLink.Messaging.IntegrationTests;

[Collection("messaging")]
public sealed class ConversationApiTests(MessagingFixture fixture)
{
    private HttpClient Client(string user)
    {
        var client = fixture.Host.CreateClient(); client.DefaultRequestHeaders.Authorization = new("Bearer", fixture.Base.Token(user)); return client;
    }
    private EventEnvelope Event(Guid engagement) => new(Guid.NewGuid(), "engagement.negotiation-opened.v1", 1, DateTimeOffset.UtcNow,
        Guid.NewGuid().ToString("N"), null, new(engagement, fixture.Base.Accounts["alice"], fixture.Base.Accounts["bob"], 1, true));
    private async Task<Guid> OpenAsync(EventEnvelope envelope)
    {
        await using var database = fixture.CreateOwnerContext();
        await new EfConversationStore(database).ConsumeAsync(envelope, default);
        return (await database.Conversations.SingleAsync(c => c.EngagementId == envelope.Data.EngagementId)).Id;
    }
    private static Task<HttpResponseMessage> SendAsync(HttpClient client, Guid id, Guid key, string body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/conversations/{id}/messages") { Content = JsonContent.Create(new MessageInput(body)) };
        request.Headers.Add("Idempotency-Key", key.ToString()); return client.SendAsync(request);
    }

    [Fact]
    public async Task Migration_assigns_sequences_to_existing_messages_without_losing_history()
    {
        var connection = await fixture.Base.DatabaseAsync("messaging");
        var options = new DbContextOptionsBuilder<MessagingDbContext>(); Infrastructure.DependencyInjection.Configure(options, connection.Owner);
        await using var db = new MessagingDbContext(options.Options);
        var initial = db.Database.GetMigrations().Single(x => x.EndsWith("_InitialMessaging", StringComparison.Ordinal));
        await db.GetService<IMigrator>().MigrateAsync(initial);
        var conversation = Guid.NewGuid(); var engagement = Guid.NewGuid(); var alice = fixture.Base.Accounts["alice"]; var bob = fixture.Base.Accounts["bob"];
        var first = Guid.NewGuid(); var second = Guid.NewGuid(); var key1 = Guid.NewGuid(); var key2 = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO messaging.conversations (\"Id\", \"EngagementId\", \"ClientId\", \"ArtistAccountId\", \"CreatedAt\", \"WritingBlocked\", \"Revision\") VALUES ({conversation}, {engagement}, {alice}, {bob}, {now}, {false}, {1L})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO messaging.messages (\"Id\", \"ConversationId\", \"AuthorId\", \"RequestKey\", \"Body\", \"CreatedAt\") VALUES ({first}, {conversation}, {alice}, {key1}, {"Primeira"}, {now.AddMinutes(-1)}), ({second}, {conversation}, {bob}, {key2}, {"Segunda"}, {now})");
        await db.Database.MigrateAsync(); db.ChangeTracker.Clear();
        var messages = await db.Messages.AsNoTracking().Where(m => m.ConversationId == conversation).OrderBy(m => m.Sequence).ToListAsync();
        Assert.Equal(new[] { 1L, 2L }, messages.Select(m => m.Sequence)); Assert.Equal(new[] { first, second }, messages.Select(m => m.Id));
        Assert.Equal(2, (await db.Conversations.AsNoTracking().SingleAsync(c => c.Id == conversation)).LastMessageSequence);
    }

    [Fact]
    public async Task Incremental_history_does_not_miss_concurrent_or_earlier_timestamp_messages()
    {
        var id = await OpenAsync(Event(Guid.NewGuid())); using var alice = Client("alice");
        var first = (await (await SendAsync(alice, id, Guid.NewGuid(), "Primeira")).Content.ReadFromJsonAsync<MessageView>())!;
        var firstPage = (await alice.GetFromJsonAsync<Page<MessageView>>($"/api/v1/conversations/{id}/messages?limit=1"))!;
        Assert.Single(firstPage.Items);
        // Even the last page provides a cursor for future incremental reads.
        Assert.False(firstPage.HasMore); Assert.NotNull(firstPage.ResumeCursor);
        var incrementalCursor = Uri.EscapeDataString(firstPage.ResumeCursor!);
        await SendAsync(alice, id, Guid.NewGuid(), "Segunda");
        firstPage = (await alice.GetFromJsonAsync<Page<MessageView>>($"/api/v1/conversations/{id}/messages?limit=1"))!;
        var cursor = Uri.EscapeDataString(firstPage.NextCursor!);
        var requests = Enumerable.Range(0, 12).Select(i => SendAsync(alice, id, Guid.NewGuid(), "Concorrente " + i)).ToArray();
        var results = await Task.WhenAll(requests);
        Assert.All(results, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        var inserted = (await results[0].Content.ReadFromJsonAsync<MessageView>())!;
        await using (var db = fixture.CreateOwnerContext())
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE messaging.messages SET \"CreatedAt\" = {first.CreatedAt.AddDays(-1)} WHERE \"Id\" = {inserted.Id}");
        var rest = (await alice.GetFromJsonAsync<Page<MessageView>>($"/api/v1/conversations/{id}/messages?limit=100&cursor={cursor}"))!;
        Assert.Equal(13, rest.Items.Count); Assert.Contains(rest.Items, m => m.Id == inserted.Id);
        Assert.Equal(rest.Items.Count, rest.Items.Select(m => m.Sequence).Distinct().Count());
        Assert.True(rest.Items.Zip(rest.Items.Skip(1), (a, b) => a.Sequence < b.Sequence).All(x => x));
        var incremental = (await alice.GetFromJsonAsync<Page<MessageView>>($"/api/v1/conversations/{id}/messages?limit=100&cursor={incrementalCursor}"))!;
        Assert.Equal(13, incremental.Items.Count); Assert.Contains(incremental.Items, m => m.Id == inserted.Id);
        Assert.NotNull(incremental.ResumeCursor);
        var empty = (await alice.GetFromJsonAsync<Page<MessageView>>($"/api/v1/conversations/{id}/messages?cursor={Uri.EscapeDataString(incremental.ResumeCursor!)}"))!;
        Assert.Empty(empty.Items); Assert.Equal(incremental.ResumeCursor, empty.ResumeCursor);
    }

    [Fact]
    public async Task Outbox_failure_is_deferred_quarantined_and_can_be_reprocessed()
    {
        await using var db = fixture.Base.CreateOwnerContext();
        var older = new InkLink.Engagements.Infrastructure.Persistence.OutboxEvent { Id = Guid.NewGuid(), EngagementId = Guid.NewGuid(), Type = "test.failure", Payload = "{}", CreatedAt = DateTimeOffset.UtcNow.AddDays(-2) };
        var newer = new InkLink.Engagements.Infrastructure.Persistence.OutboxEvent { Id = Guid.NewGuid(), EngagementId = Guid.NewGuid(), Type = "test.healthy", Payload = "{}", CreatedAt = DateTimeOffset.UtcNow.AddDays(-1) };
        older.RecordFailure("Failure", DateTimeOffset.UtcNow);
        db.Outbox.AddRange(older, newer); await db.SaveChangesAsync();
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            var next = await new InkLink.Engagements.Infrastructure.Persistence.OutboxQueue(db).LockNextAsync(default);
            Assert.Equal(newer.Id, next!.Id); await transaction.RollbackAsync();
        }
        for (var i = 1; i < 10; i++) older.RecordFailure("Failure", DateTimeOffset.UtcNow);
        Assert.NotNull(older.QuarantinedAt); Assert.Null(older.NextAttemptAt);
        older.ResetForRetry(); Assert.Null(older.QuarantinedAt); Assert.Equal(0, older.Attempts);
        Assert.Null(older.PublishedAt);
    }

    [Fact]
    public async Task Engagement_and_outbox_commit_together_and_rollback_together()
    {
        await using var db = fixture.Base.CreateOwnerContext();
        var account = fixture.Base.Accounts["alice"];
        var engagement = InkLink.Engagements.Domain.Engagement.Create(account, fixture.Base.Accounts["bob"], Guid.NewGuid(), null,
            "Ideia", "Braço", 10, "{}", Guid.NewGuid(), "fixture", DateTimeOffset.UtcNow);
        var transaction = await db.Database.BeginTransactionAsync();
        await new InkLink.Engagements.Infrastructure.Persistence.EfEngagementStore(db).CreateAsync(engagement, default);
        Assert.True(await db.Outbox.AnyAsync(e => e.EngagementId == engagement.Id));
        await transaction.RollbackAsync(); await transaction.DisposeAsync(); db.ChangeTracker.Clear();
        Assert.False(await db.Outbox.AnyAsync(e => e.EngagementId == engagement.Id));
        Assert.False(await db.Engagements.AnyAsync(e => e.Id == engagement.Id));
        await new InkLink.Engagements.Infrastructure.Persistence.EfEngagementStore(db).CreateAsync(engagement, default);
        Assert.True(await db.Engagements.AnyAsync(e => e.Id == engagement.Id));
        Assert.Equal(1, await db.Outbox.CountAsync(e => e.EngagementId == engagement.Id));
    }

    [Fact]
    public async Task Duplicate_events_are_idempotent_and_incompatible_participants_roll_back_inbox()
    {
        var envelope = Event(Guid.NewGuid()); var id = await OpenAsync(envelope);
        await using var db = fixture.CreateOwnerContext(); var store = new EfConversationStore(db);
        await store.ConsumeAsync(envelope, default);
        Assert.Equal(1, await db.Conversations.CountAsync(c => c.EngagementId == envelope.Data.EngagementId));
        Assert.Equal(1, await db.Inbox.CountAsync(i => i.Id == envelope.MessageId));
        var wrong = envelope with { MessageId = Guid.NewGuid(), Data = envelope.Data with { ClientId = Guid.NewGuid() } };
        await Assert.ThrowsAsync<InkLink.Messaging.Domain.InvalidConversationData>(() => store.ConsumeAsync(wrong, default));
        db.ChangeTracker.Clear(); Assert.False(await db.Inbox.AnyAsync(i => i.Id == wrong.MessageId));
        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public async Task Both_participants_share_persistent_history_and_third_party_is_denied()
    {
        var envelope = Event(Guid.NewGuid()); var id = await OpenAsync(envelope);
        using var alice = Client("alice"); using var bob = Client("bob"); using var carol = Client("carol");
        var key = Guid.NewGuid(); var first = await SendAsync(alice, id, key, "  Olá!  "); Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var message = (await first.Content.ReadFromJsonAsync<MessageView>())!; Assert.Equal("Olá!", message.Body); Assert.Equal(fixture.Base.Accounts["alice"], message.AuthorId);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(alice, id, key, "Olá!")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(alice, id, key, "Outra")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(alice, id, Guid.NewGuid(), "  ")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(bob, id, Guid.NewGuid(), "Resposta")).StatusCode);
        var history = (await bob.GetFromJsonAsync<Page<MessageView>>($"/api/v1/conversations/{id}/messages?limit=1"))!;
        Assert.Single(history.Items); Assert.True(history.HasMore); Assert.Equal(2, history.Total);
        var cursor = Uri.EscapeDataString(history.NextCursor!);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.GetAsync($"/api/v1/conversations/{id}/messages?limit=1&cursor={cursor}")).StatusCode);
        var next = (await bob.GetFromJsonAsync<Page<MessageView>>($"/api/v1/conversations/{id}/messages?limit=1&cursor={cursor}"))!;
        Assert.Single(next.Items); Assert.NotEqual(history.Items[0].Id, next.Items[0].Id);
        Assert.Equal(HttpStatusCode.NotFound, (await carol.GetAsync($"/api/v1/conversations/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(carol, id, Guid.NewGuid(), "Intrusão")).StatusCode);
        var detail = (await bob.GetFromJsonAsync<ConversationView>($"/api/v1/conversations/{id}"))!;
        Assert.Equal("Resposta", detail.LastMessage!.Body);
        using var restarted = new MessagingHost(fixture); using var reopened = restarted.CreateClient();
        reopened.DefaultRequestHeaders.Authorization = new("Bearer", fixture.Base.Token("alice"));
        var persisted = (await reopened.GetFromJsonAsync<Page<MessageView>>($"/api/v1/conversations/{id}/messages"))!;
        Assert.Equal(new[] { "Olá!", "Resposta" }, persisted.Items.Select(m => m.Body));
    }

    [Fact]
    public async Task Technical_block_endpoint_accepts_only_its_service_scope()
    {
        var envelope = Event(Guid.NewGuid()); var id = await OpenAsync(envelope);
        using var user = Client("alice");
        var path = $"/api/v1/internal/engagements/{envelope.Data.EngagementId}/writing-block";
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PutAsync(path, null)).StatusCode);
        using var service = fixture.Host.CreateClient();
        service.DefaultRequestHeaders.Authorization = new("Bearer", fixture.Base.Token("service", "service", "inklink-catalog", "messaging.conversation.block"));
        Assert.Equal(HttpStatusCode.Forbidden, (await service.PutAsync(path, null)).StatusCode);
        service.DefaultRequestHeaders.Authorization = new("Bearer", fixture.Base.Token("service", "service", "inklink-engagements", "messaging.conversation.block"));
        Assert.Equal(HttpStatusCode.NoContent, (await service.PutAsync(path, null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await service.PutAsync(path, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(user, id, Guid.NewGuid(), "Bloqueada")).StatusCode);
    }

    [Fact]
    public async Task Blocking_and_sending_are_serialized_and_old_events_cannot_reopen()
    {
        var envelope = Event(Guid.NewGuid()); var id = await OpenAsync(envelope); using var alice = Client("alice");
        await using var database = fixture.CreateOwnerContext();
        var block = new EfConversationStore(database).BlockAsync(envelope.Data.EngagementId, default);
        var send = SendAsync(alice, id, Guid.NewGuid(), "Durante o bloqueio");
        await Task.WhenAll(block, send);
        Assert.Contains((await send).StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.Conflict });
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(alice, id, Guid.NewGuid(), "Depois")).StatusCode);
        await new EfConversationStore(database).BlockAsync(envelope.Data.EngagementId, default);
        await new EfConversationStore(database).ConsumeAsync(envelope with { MessageId = Guid.NewGuid() }, default);
        Assert.True((await database.Conversations.SingleAsync(c => c.Id == id)).WritingBlocked);
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(alice, id, Guid.NewGuid(), "Ainda bloqueada")).StatusCode);
    }
}
