using System.Net;
using System.Net.Http.Json;
using InkLink.Engagements.IntegrationTests;
using InkLink.Messaging.Application.Accounts;
using InkLink.Messaging.Application.Conversations;
using InkLink.Messaging.Infrastructure.Integration;
using InkLink.Messaging.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InkLink.Messaging.IntegrationTests;

public sealed class MessagingFixture : IAsyncLifetime
{
    public EngagementFixture Base { get; } = new();
    public string OwnerConnection { get; private set; } = string.Empty;
    public string AppConnection { get; private set; } = string.Empty;
    public string ServiceSecret { get; } = Guid.NewGuid().ToString("N");
    public string ClientSecret => Base.RealProvider?.MessagingSecret ?? ServiceSecret;
    public MessagingHost Host { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await Base.InitializeAsync();
        var connections = await Base.DatabaseAsync("messaging"); OwnerConnection = connections.Owner; AppConnection = connections.App;
        await using var database = CreateOwnerContext();
        await database.Database.MigrateAsync(); await database.Database.MigrateAsync();
        Host = new MessagingHost(this);
    }
    public MessagingDbContext CreateOwnerContext()
    {
        var options = new DbContextOptionsBuilder<MessagingDbContext>();
        Infrastructure.DependencyInjection.Configure(options, OwnerConnection);
        return new(options.Options);
    }
    public async Task DisposeAsync() { Host?.Dispose(); await Base.DisposeAsync(); }
}

[CollectionDefinition("messaging")]
public sealed class MessagingCollection : ICollectionFixture<MessagingFixture>;

public sealed class MessagingHost(MessagingFixture fixture) : WebApplicationFactory<MessagingProgram>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        foreach (var setting in new Dictionary<string, string?>
        {
            ["MessagingDatabase:ConnectionString"] = fixture.AppConnection,
            ["Authentication:Authority"] = fixture.Base.Authority,
            ["Authentication:Audience"] = "inklink-api",
            ["Authentication:UserClients:0"] = fixture.Base.UserClient,
            ["Messaging:IdentityAddress"] = "http://identity.test/",
            ["Messaging:EngagementsAddress"] = "http://engagements.test/",
            ["Messaging:ClientSecret"] = fixture.ClientSecret
        }) builder.UseSetting(setting.Key, setting.Value);
        builder.ConfigureTestServices(services =>
        {
            services.AddHttpClient<IConversationPresentationDirectory, ConversationPresentationDirectory>().ConfigurePrimaryHttpMessageHandler(() => fixture.Base.Engagements.Server.CreateHandler());
            services.AddHttpClient<ParticipantProfiles>().ConfigurePrimaryHttpMessageHandler(() => fixture.Base.Identity.Server.CreateHandler());
            services.AddHttpClient<MessagingTokenClient>().ConfigurePrimaryHttpMessageHandler(() => fixture.Base.RealProvider is null ? new MessagingTokenHandler(fixture) : fixture.Base.RealProvider.CreateProviderHandler());
            services.Configure<JwtBearerOptions>("Bearer", fixture.Base.ValidateTokens);
            services.AddHttpClient<IIdentityDirectory, IdentityDirectory>().ConfigurePrimaryHttpMessageHandler(() => fixture.Base.Identity.Server.CreateHandler());
        });
    }
}


internal sealed class MessagingTokenHandler(MessagingFixture fixture) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var form = await request.Content!.ReadAsStringAsync(cancellationToken);
        Assert.Contains("client_id=inklink-messaging", form);
        Assert.True(form.Contains("client_secret=" + fixture.ClientSecret, StringComparison.Ordinal), "Messaging test credential mismatch.");
        Assert.Contains("scope=identity.profile.read", form);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { access_token = fixture.Base.Token("service-account", "service", "inklink-messaging", "identity.profile.read") }) };
    }
}
