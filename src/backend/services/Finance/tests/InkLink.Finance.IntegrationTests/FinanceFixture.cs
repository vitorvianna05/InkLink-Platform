using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using InkLink.Finance.Application.Ledger;
using InkLink.Finance.Infrastructure.Integration;
using InkLink.Finance.Infrastructure.Persistence;
using InkLink.Messaging.Infrastructure.Persistence;
using InkLink.Scheduling.IntegrationTests;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace InkLink.Finance.IntegrationTests;

public sealed class MutableClock : TimeProvider
{
    private DateTimeOffset now = DateTimeOffset.UtcNow;
    public void Set(DateTimeOffset value) => now = value.ToUniversalTime();
    public override DateTimeOffset GetUtcNow() => now;
}
public sealed class FinanceFixture : IAsyncLifetime
{
    public SchedulingFixture Base { get; } = new();
    public MutableClock Clock { get; } = new();
    public string Owner { get; private set; } = "";
    public string App { get; private set; } = "";
    public string MessagingOwner { get; private set; } = "";
    public string MessagingApp { get; private set; } = "";
    public WebApplicationFactory<SchedulingProgram> Scheduling { get; private set; } = null!;
    public FinanceHost Host { get; private set; } = null!;
    public ChatHost Messaging { get; private set; } = null!;
    public string? UnavailableHost { get; set; }
    public (string Host, string Path, string Field, bool SetNull)? SourceFieldFault { get; set; }
    private int counter;
    public DateOnly NextDay() { Clock.Set(DateTimeOffset.UtcNow); UnavailableHost = null; SourceFieldFault = null; return DateOnly.FromDateTime(DateTime.UtcNow).AddDays(20 + 10 * Interlocked.Increment(ref counter)); }
    public async Task InitializeAsync()
    {
        await Base.InitializeAsync();
        var finance = await Base.Base.DatabaseAsync("finance"); Owner = finance.Owner; App = finance.App;
        await using (var db = Context()) { await db.Database.MigrateAsync(); await db.Database.MigrateAsync(); }
        var chat = await Base.Base.DatabaseAsync("messaging"); MessagingOwner = chat.Owner; MessagingApp = chat.App;
        await using (var db = ChatContext()) await db.Database.MigrateAsync();
        Scheduling = Base.Host.WithWebHostBuilder(b => b.ConfigureTestServices(s => { s.RemoveAll<TimeProvider>(); s.AddSingleton<TimeProvider>(Clock); }));
        Messaging = new(this); Host = new(this);
    }
    public FinanceDbContext Context()
    {
        var options = new DbContextOptionsBuilder<FinanceDbContext>();
        Infrastructure.DependencyInjection.Configure(options, Owner); return new(options.Options);
    }
    public MessagingDbContext ChatContext()
    {
        var options = new DbContextOptionsBuilder<MessagingDbContext>();
        InkLink.Messaging.Infrastructure.DependencyInjection.Configure(options, MessagingOwner); return new(options.Options);
    }
    public HttpClient Client(string? user = "bob")
    {
        var client = Host.CreateClient(); if (user is not null) client.DefaultRequestHeaders.Authorization = new("Bearer", Base.Base.Token(user)); return client;
    }
    public HttpClient ScheduleClient(string user = "bob")
    {
        var client = Scheduling.CreateClient(); client.DefaultRequestHeaders.Authorization = new("Bearer", Base.Base.Token(user)); return client;
    }
    public async Task DisposeAsync()
    { Host?.Dispose(); Messaging?.Dispose(); Scheduling?.Dispose(); await Base.DisposeAsync(); }
}
[CollectionDefinition("finance")]
public sealed class FinanceCollection : ICollectionFixture<FinanceFixture>;
public sealed class FinanceHost(FinanceFixture f) : WebApplicationFactory<FinanceProgram>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        foreach (var pair in new Dictionary<string, string?>
        {
            ["FinanceDatabase:ConnectionString"] = f.App,
            ["Authentication:Authority"] = f.Base.Base.Authority,
            ["Authentication:Audience"] = "inklink-api",
            ["Authentication:UserClients:0"] = f.Base.Base.UserClient,
            ["Finance:IdentityAddress"] = "http://identity.test/",
            ["Finance:SchedulingAddress"] = "http://scheduling.test/",
            ["Finance:MessagingAddress"] = "http://messaging.test/",
            ["Finance:EngagementsAddress"] = "http://engagements.test/"
        }) builder.UseSetting(pair.Key, pair.Value);
        builder.ConfigureTestServices(services =>
        {
            services.Configure<JwtBearerOptions>("Bearer", f.Base.Base.ValidateTokens);
            services.AddHttpClient<InkLink.Finance.Application.Accounts.IIdentityDirectory, IdentityDirectory>().ConfigurePrimaryHttpMessageHandler(() => f.Base.Base.Identity.Server.CreateHandler());
            services.AddHttpClient<IFinanceSources, FinanceSources>().ConfigurePrimaryHttpMessageHandler(() => new SourceRouter(f));
            services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(f.Clock);
        });
    }
}
public sealed class ChatHost(FinanceFixture f) : WebApplicationFactory<MessagingProgram>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        foreach (var pair in new Dictionary<string, string?>
        {
            ["MessagingDatabase:ConnectionString"] = f.MessagingApp,
            ["Authentication:Authority"] = f.Base.Base.Authority,
            ["Authentication:Audience"] = "inklink-api",
            ["Authentication:UserClients:0"] = f.Base.Base.UserClient,
            ["Messaging:IdentityAddress"] = "http://identity.test/",
            ["Messaging:EngagementsAddress"] = "http://engagements.test/",
            ["Messaging:ClientSecret"] = "test-only"
        }) builder.UseSetting(pair.Key, pair.Value);
        builder.ConfigureTestServices(services =>
        {
            services.Configure<JwtBearerOptions>("Bearer", f.Base.Base.ValidateTokens);
            services.AddHttpClient<InkLink.Messaging.Application.Accounts.IIdentityDirectory, InkLink.Messaging.Infrastructure.Integration.IdentityDirectory>()
                .ConfigurePrimaryHttpMessageHandler(() => f.Base.Base.Identity.Server.CreateHandler());
            services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(f.Clock);
        });
    }
}
internal sealed class SourceRouter(FinanceFixture f) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.RequestUri!.Host == f.UnavailableHost) return new(HttpStatusCode.ServiceUnavailable);
        if ("invalid:" + request.RequestUri.Host == f.UnavailableHost)
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { artistId = Guid.NewGuid(), activeConversations = -1, observedAt = f.Clock.GetUtcNow() }) };
        using var invoker = new HttpMessageInvoker(request.RequestUri.Host switch
        {
            "scheduling.test" => f.Scheduling.Server.CreateHandler(),
            "messaging.test" => f.Messaging.Server.CreateHandler(),
            "engagements.test" => f.Base.Base.Engagements.Server.CreateHandler(),
            _ => throw new InvalidOperationException("Unexpected backend.")
        });
        var response = await invoker.SendAsync(request, ct);
        if (f.SourceFieldFault is { } fault && request.RequestUri.Host == fault.Host && request.RequestUri.AbsolutePath == fault.Path)
        {
            using var original = response.Content;
            var body = (await original.ReadFromJsonAsync<JsonObject>(ct))!;
            if (fault.SetNull) body[fault.Field] = null;
            else body.Remove(fault.Field);
            response.Content = JsonContent.Create(body);
        }
        return response;
    }
}
