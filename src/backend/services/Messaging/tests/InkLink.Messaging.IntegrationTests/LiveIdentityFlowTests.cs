using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using InkLink.Engagements.Application.Negotiations;
using InkLink.Identity.IntegrationTests;
using InkLink.Messaging.Application.Conversations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Forwarder;

namespace InkLink.Messaging.IntegrationTests;

[Collection("messaging")]
public sealed class LiveIdentityFlowTests
{
    [Fact]
    public async Task Real_Keycloak_and_RabbitMQ_preserve_perspectives_recovery_and_technical_authorization()
    {
        var provider = new SecurityFixture();
        var fixture = new MessagingFixture();
        await using var broker = new BrokerHarness();
        try
        {
            await provider.InitializeAsync();
            fixture.Base.UseRealIdentity(provider);
            await fixture.InitializeAsync();
            await broker.InitializeAsync();
            using var publisher = fixture.Base.NewHost().WithWebHostBuilder(builder => builder.UseSetting("Broker:Uri", broker.PublisherUri).UseSetting("Broker:EnableWorkers", "true"));
            using var catalog = fixture.Base.Catalog.CreateClient(); catalog.DefaultRequestHeaders.Authorization = new("Bearer", fixture.Base.Token("bob"));
            var published = await catalog.PutAsJsonAsync("/api/v1/artists/me", new { name = "Bob Público", studio = "Estúdio", city = "São Paulo", biography = "Bio", styles = new[] { "Blackwork" }, startingPrice = 300 });
            Assert.True(published.IsSuccessStatusCode);
            var artist = (await published.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<Guid>();
            using var aliceEngagements = publisher.CreateClient(); aliceEngagements.DefaultRequestHeaders.Authorization = new("Bearer", fixture.Base.Token("alice"));
            var request = await PostAsync(aliceEngagements, "/api/v2/engagements", new RequestInput(artist, "Negociação real", "Braço", 10));
            Assert.Equal(HttpStatusCode.Created, request.StatusCode);
            var engagement = (await request.Content.ReadFromJsonAsync<EngagementView>())!;
            await BrokerHarness.UntilAsync(async () =>
            {
                await using var db = fixture.Base.CreateOwnerContext();
                return await db.Outbox.AnyAsync(item => item.EngagementId == engagement.Id && item.PublishedAt != null);
            }, "Real token request and publication did not complete.");
            // Simulate a consumer interruption after receipt but before acknowledgement/commit.
            using (var connection = new RabbitMQ.Client.ConnectionFactory { Uri = new(broker.ConsumerUri) }.CreateConnection())
            {
                using (var channel = connection.CreateModel())
                {
                    Assert.NotNull(channel.BasicGet("inklink.messaging.opened.v1", autoAck: false));
                }
                using (var channel = connection.CreateModel())
                {
                    var repeated = channel.BasicGet("inklink.messaging.opened.v1", autoAck: false);
                    Assert.NotNull(repeated); Assert.True(repeated.Redelivered);
                }
            }
            using var messaging = fixture.Host.WithWebHostBuilder(builder => builder.UseSetting("Broker:Uri", broker.ConsumerUri).UseSetting("Broker:EnableWorkers", "true"));
            using var alice = messaging.CreateClient(); alice.DefaultRequestHeaders.Authorization = new("Bearer", fixture.Base.Token("alice"));
            using var bob = messaging.CreateClient(); bob.DefaultRequestHeaders.Authorization = new("Bearer", fixture.Base.Token("bob"));
            using var carol = messaging.CreateClient(); carol.DefaultRequestHeaders.Authorization = new("Bearer", fixture.Base.Token("carol"));
            await BrokerHarness.UntilAsync(async () =>
            {
                await using var db = fixture.CreateOwnerContext(); return await db.Conversations.AnyAsync(item => item.EngagementId == engagement.Id);
            }, "Redelivery after interruption did not create the conversation.");
            Guid id;
            await using (var db = fixture.CreateOwnerContext()) id = (await db.Conversations.SingleAsync(item => item.EngagementId == engagement.Id)).Id;
            var path = $"/api/v1/conversations/{id}";
            var clientView = (await alice.GetFromJsonAsync<ConversationView>(path))!;
            Assert.Equal("Bob Público", clientView.Counterpart.DisplayName); Assert.Equal("Negotiating", clientView.EngagementState);
            Assert.Equal(HttpStatusCode.NotFound, (await carol.GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(carol, path + "/messages", new MessageInput("Intrusão"))).StatusCode);
            var messageKey = Guid.NewGuid();
            Assert.Equal(HttpStatusCode.Created, (await PostAsync(alice, path + "/messages", new MessageInput("Olá real"), messageKey)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await PostAsync(alice, path + "/messages", new MessageInput("Olá real"), messageKey)).StatusCode);
            Assert.Equal(HttpStatusCode.Created, (await PostAsync(bob, path + "/messages", new MessageInput("Resposta real"))).StatusCode);
            using (var identity = fixture.Base.Identity.CreateClient())
            {
                identity.DefaultRequestHeaders.Authorization = new("Bearer", fixture.Base.Token("alice"));
                Assert.Equal(HttpStatusCode.NoContent, (await identity.PutAsJsonAsync("/api/v1/profiles/me", new { displayName = "Alice atualizada" })).StatusCode);
            }
            var artistView = (await bob.GetFromJsonAsync<ConversationView>(path))!;
            Assert.Equal("Alice atualizada", artistView.Counterpart.DisplayName); Assert.Equal("Resposta real", artistView.LastMessage!.Body);
            using var artistEngagements = publisher.CreateClient(); artistEngagements.DefaultRequestHeaders.Authorization = new("Bearer", fixture.Base.Token("bob"));
            var proposalResponse = await PostAsync(artistEngagements, $"/api/v2/engagements/{engagement.Id}/proposals", new ProposalInput("Arte", 450));
            Assert.Equal(HttpStatusCode.Created, proposalResponse.StatusCode);
            var proposal = (await proposalResponse.Content.ReadFromJsonAsync<ProposalView>())!;
            Assert.Equal(HttpStatusCode.OK, (await aliceEngagements.PutAsync($"/api/v2/engagements/{engagement.Id}/proposals/{proposal.Id}/acceptance", null)).StatusCode);
            var agreed = (await alice.GetFromJsonAsync<ConversationView>(path))!;
            Assert.Equal("Agreed", agreed.EngagementState); Assert.False(agreed.WritingBlocked);
            using var gateway = fixture.Base.Gateway().WithWebHostBuilder(builder => builder.UseSetting("Gateway:MessagingAddress", "http://messaging.test/")
                .ConfigureTestServices(services => services.AddSingleton<IForwarderHttpClientFactory>(new MessagingProxyClient(messaging.Server.CreateHandler()))));
            using var gatewayClient = gateway.CreateClient(); gatewayClient.DefaultRequestHeaders.Authorization = new("Bearer", fixture.Base.Token("bob"));
            var list = (await gatewayClient.GetFromJsonAsync<InkLink.Messaging.Application.Conversations.Page<ConversationView>>("/api/v1/conversations"))!;
            Assert.Contains(list.Items, item => item.Id == id && item.Counterpart.DisplayName == "Alice atualizada" && item.EngagementState == "Agreed");
            var block = $"/api/v1/internal/engagements/{engagement.Id}/writing-block";
            Assert.Equal(HttpStatusCode.NotFound, (await gatewayClient.PutAsync(block, null)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await alice.PutAsync(block, null)).StatusCode);
            using var service = messaging.CreateClient();
            var catalogToken = await ServiceTokenAsync(provider, "inklink-catalog", provider.ServiceSecret, "identity.profile.read");
            service.DefaultRequestHeaders.Authorization = new("Bearer", catalogToken);
            Assert.Equal(HttpStatusCode.Forbidden, (await service.PutAsync(block, null)).StatusCode);
            var noScope = await ServiceTokenAsync(provider, "inklink-engagements", provider.EngagementsSecret, null);
            service.DefaultRequestHeaders.Authorization = new("Bearer", noScope);
            Assert.Equal(HttpStatusCode.Forbidden, (await service.PutAsync(block, null)).StatusCode);
            var blockToken = await ServiceTokenAsync(provider, "inklink-engagements", provider.EngagementsSecret, "messaging.conversation.block");
            service.DefaultRequestHeaders.Authorization = new("Bearer", blockToken);
            Assert.Equal(HttpStatusCode.NoContent, (await service.PutAsync(block, null)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await service.PutAsync(block, null)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(alice, path + "/messages", new MessageInput("Após bloqueio"))).StatusCode);
            string opening;
            await using (var db = fixture.Base.CreateOwnerContext()) opening = (await db.Outbox.SingleAsync(item => item.EngagementId == engagement.Id)).Payload;
            var delayed = JsonNode.Parse(opening)!; var delayedId = Guid.NewGuid(); delayed["messageId"] = delayedId;
            broker.Publish(System.Text.Encoding.UTF8.GetBytes(delayed.ToJsonString()));
            await BrokerHarness.UntilAsync(async () => { await using var db = fixture.CreateOwnerContext(); return await db.Inbox.AnyAsync(item => item.Id == delayedId); }, "Delayed opening was not deduplicated after blocking.");
            Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(alice, path + "/messages", new MessageInput("Continua bloqueada"))).StatusCode);
            var blocked = (await bob.GetFromJsonAsync<ConversationView>(path))!;
            Assert.True(blocked.WritingBlocked); Assert.Equal("Agreed", blocked.EngagementState);
            var history = (await bob.GetFromJsonAsync<InkLink.Messaging.Application.Conversations.Page<MessageView>>(path + "/messages"))!;
            Assert.Equal(new[] { "Olá real", "Resposta real" }, history.Items.Select(item => item.Body));
            // A new host reads the same durable history after the worker host is gone.
            messaging.Dispose();
            using var reopened = new MessagingHost(fixture); using var recovered = reopened.CreateClient();
            recovered.DefaultRequestHeaders.Authorization = new("Bearer", fixture.Base.Token("alice"));
            Assert.Equal(2, (await recovered.GetFromJsonAsync<InkLink.Messaging.Application.Conversations.Page<MessageView>>(path + "/messages"))!.Total);
        }
        finally { await fixture.DisposeAsync(); await provider.DisposeAsync(); }
    }

    private static async Task<HttpResponseMessage> PostAsync<T>(HttpClient client, string path, T input, Guid? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(input) };
        request.Headers.Add("Idempotency-Key", (key ?? Guid.NewGuid()).ToString());
        return await client.SendAsync(request);
    }
    private static async Task<string> ServiceTokenAsync(SecurityFixture provider, string client, string secret, string? scope)
    {
        using var http = new HttpClient(provider.CreateProviderHandler());
        var form = new Dictionary<string, string> { ["grant_type"] = "client_credentials", ["client_id"] = client, ["client_secret"] = secret };
        if (scope is not null) form["scope"] = scope;
        using var response = await http.PostAsync(provider.Authority + "/protocol/openid-connect/token", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!["access_token"]!.GetValue<string>();
    }
}

internal sealed class MessagingProxyClient(HttpMessageHandler handler) : IForwarderHttpClientFactory
{
    public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) => new(handler, disposeHandler: false);
}
