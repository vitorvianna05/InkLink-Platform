using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using DotNet.Testcontainers.Builders;
using InkLink.Engagements.Application.Negotiations;
using InkLink.Messaging.Application.Conversations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace InkLink.Messaging.IntegrationTests;

[Collection("messaging")]
public sealed class RabbitFlowTests(MessagingFixture fixture)
{
    [Fact]
    public async Task Broker_outage_recovers_outbox_and_creates_one_shared_conversation()
    {
        var password = Guid.NewGuid().ToString("N");
        await using var broker = new ContainerBuilder("rabbitmq:3.13.7-alpine")
            .WithEnvironment("RABBITMQ_DEFAULT_USER", "inklink")
            .WithEnvironment("RABBITMQ_DEFAULT_PASS", password)
            .WithPortBinding(5672, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Server startup complete"))
            .Build();
        await broker.StartAsync();
        var uri = $"amqp://inklink:{password}@{broker.Hostname}:{broker.GetMappedPublicPort(5672)}/";
        await broker.StopAsync();
        using var publisher = fixture.Base.NewHost().WithWebHostBuilder(builder => builder.UseSetting("Broker:Uri", uri).UseSetting("Broker:EnableWorkers", "true"));
        using var consumer = fixture.Host.WithWebHostBuilder(builder => builder.UseSetting("Broker:Uri", uri).UseSetting("Broker:EnableWorkers", "true"));
        using var catalog = fixture.Base.Catalog.CreateClient(); catalog.DefaultRequestHeaders.Authorization = new("Bearer", fixture.Base.Token("bob"));
        var published = await catalog.PutAsJsonAsync("/api/v1/artists/me", new { name = "Bob", studio = "Estúdio", city = "São Paulo", biography = "Portfólio", styles = new[] { "Blackwork" }, startingPrice = 300 });
        var artist = (await published.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<Guid>();
        using var requestClient = publisher.CreateClient(); requestClient.DefaultRequestHeaders.Authorization = new("Bearer", fixture.Base.Token("alice"));
        using var conversations = consumer.CreateClient(); conversations.DefaultRequestHeaders.Authorization = new("Bearer", fixture.Base.Token("alice"));
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v2/engagements") { Content = JsonContent.Create(new RequestInput(artist, "Teste", "Braço", 10)) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var response = await requestClient.SendAsync(request); Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var engagement = (await response.Content.ReadFromJsonAsync<EngagementView>())!;
        await using (var db = fixture.Base.CreateOwnerContext())
            Assert.Null((await db.Outbox.SingleAsync(e => e.EngagementId == engagement.Id)).PublishedAt);
        await broker.StartAsync();
        var deadline = DateTime.UtcNow.AddSeconds(45);
        ConversationView? conversation = null;
        while (DateTime.UtcNow < deadline && conversation is null)
        {
            var page = await conversations.GetFromJsonAsync<InkLink.Messaging.Application.Conversations.Page<ConversationView>>("/api/v1/conversations?limit=100");
            conversation = page!.Items.SingleOrDefault(c => c.EngagementId == engagement.Id);
            if (conversation is null) await Task.Delay(500);
        }
        Assert.NotNull(conversation);
        await using (var db = fixture.Base.CreateOwnerContext())
            Assert.NotNull((await db.Outbox.SingleAsync(e => e.EngagementId == engagement.Id)).PublishedAt);
        await using (var db = fixture.CreateOwnerContext())
            Assert.Equal(1, await db.Conversations.CountAsync(c => c.EngagementId == engagement.Id));
    }
}
