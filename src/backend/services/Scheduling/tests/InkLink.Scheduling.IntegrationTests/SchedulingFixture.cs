using System.Net;
using System.Net.Http.Json;
using InkLink.Engagements.IntegrationTests;
using InkLink.Scheduling.Application.Accounts;
using InkLink.Scheduling.Application.Reservations;
using InkLink.Scheduling.Infrastructure.Integration;
using InkLink.Scheduling.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InkLink.Scheduling.IntegrationTests;

public sealed class SchedulingFixture : IAsyncLifetime
{
    public EngagementFixture Base { get; } = new();
    public string OwnerConnection { get; private set; } = "";
    public string AppConnection { get; private set; } = "";
    public string Secret => Base.RealProvider?.SchedulingSecret ?? "fixture-scheduling-secret";
    public SchedulingHost Host { get; private set; } = null!;
    public async Task InitializeAsync()
    {
        await Base.InitializeAsync();
        var connection = await Base.DatabaseAsync("scheduling"); OwnerConnection = connection.Owner; AppConnection = connection.App;
        await using var db = CreateOwnerContext(); await db.Database.MigrateAsync(); await db.Database.MigrateAsync();
        Host = new(this);
    }
    public SchedulingDbContext CreateOwnerContext()
    {
        var options = new DbContextOptionsBuilder<SchedulingDbContext>();
        Infrastructure.DependencyInjection.Configure(options, OwnerConnection); return new(options.Options);
    }
    public async Task DisposeAsync() { Host?.Dispose(); await Base.DisposeAsync(); }
}
[CollectionDefinition("scheduling")]
public sealed class SchedulingCollection : ICollectionFixture<SchedulingFixture>;

public sealed class SchedulingHost(SchedulingFixture fixture) : WebApplicationFactory<SchedulingProgram>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        foreach (var setting in new Dictionary<string, string?>
        {
            ["SchedulingDatabase:ConnectionString"] = fixture.AppConnection,
            ["Authentication:Authority"] = fixture.Base.Authority,
            ["Authentication:Audience"] = "inklink-api",
            ["Authentication:UserClients:0"] = fixture.Base.UserClient,
            ["Scheduling:IdentityAddress"] = "http://identity.test/",
            ["Scheduling:EngagementsAddress"] = "http://engagements.test/",
            ["Scheduling:ClientSecret"] = fixture.Secret
        }) builder.UseSetting(setting.Key, setting.Value);
        builder.ConfigureTestServices(services =>
        {
            services.Configure<JwtBearerOptions>("Bearer", fixture.Base.ValidateTokens);
            services.AddHttpClient<IIdentityDirectory, IdentityDirectory>().ConfigurePrimaryHttpMessageHandler(() => fixture.Base.Identity.Server.CreateHandler());
            services.AddHttpClient<IEngagementDirectory, EngagementDirectory>().ConfigurePrimaryHttpMessageHandler(() => fixture.Base.Engagements.Server.CreateHandler());
            services.AddHttpClient<IParticipantDirectory, ParticipantDirectory>().ConfigurePrimaryHttpMessageHandler(() => fixture.Base.Identity.Server.CreateHandler());
            services.AddHttpClient<SchedulingTokenClient>().ConfigurePrimaryHttpMessageHandler(() => fixture.Base.RealProvider is null ? new TokenHandler(fixture) : fixture.Base.RealProvider.CreateProviderHandler());
        });
    }
}
internal sealed class TokenHandler(SchedulingFixture fixture) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = await request.Content!.ReadAsStringAsync(ct);
        Assert.Contains("client_id=inklink-scheduling", body);
        Assert.True(body.Contains("client_secret=" + fixture.Secret, StringComparison.Ordinal), "Fixture credential mismatch.");
        var scope = body.Contains("identity.profile.read", StringComparison.Ordinal) ? "identity.profile.read" : "engagements.scheduling-context.read";
        return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { access_token = fixture.Base.Token("scheduling", "service", "inklink-scheduling", scope) }) };
    }
}
