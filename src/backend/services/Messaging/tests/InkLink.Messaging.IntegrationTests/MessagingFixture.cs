using InkLink.Engagements.IntegrationTests;
using InkLink.Messaging.Application.Accounts;
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
            ["Authentication:Authority"] = EngagementFixture.Issuer,
            ["Authentication:Audience"] = "inklink-api",
            ["Authentication:UserClients:0"] = "p04-users",
            ["Messaging:IdentityAddress"] = "http://identity.test/"
        }) builder.UseSetting(setting.Key, setting.Value);
        builder.ConfigureTestServices(services =>
        {
            services.Configure<JwtBearerOptions>("Bearer", fixture.Base.ValidateTokens);
            services.AddHttpClient<IIdentityDirectory, IdentityDirectory>().ConfigurePrimaryHttpMessageHandler(() => fixture.Base.Identity.Server.CreateHandler());
        });
    }
}
