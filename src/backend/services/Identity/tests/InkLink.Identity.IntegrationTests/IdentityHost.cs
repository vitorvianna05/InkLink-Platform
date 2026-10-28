using InkLink.Identity.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace InkLink.Identity.IntegrationTests;

internal sealed class IdentityHost(string connection, Guid? caller = null, bool failRoute = false) : WebApplicationFactory<IdentityProgram>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Authentication:Authority", "http://127.0.0.1:1/realms/p01-controlled");
        builder.UseSetting("Authentication:Audience", "inklink-api");
        builder.UseSetting("Authentication:UserClients:0", "p01-controlled");
        builder.UseSetting("Authentication:AllowHttpMetadata", "true");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["IdentityDatabase:ConnectionString"] = connection,
                ["Authentication:Authority"] = "http://127.0.0.1:1/realms/p01-controlled",
                ["Authentication:Audience"] = "inklink-api",
                ["Authentication:UserClients:0"] = "p01-controlled",
                ["Authentication:AllowHttpMetadata"] = "true"
            }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICurrentUser>();
            services.AddScoped<ICurrentUser>(_ => new Current(caller));
            if (failRoute) services.AddTransient<IStartupFilter, FailureFilter>();
        });
    }
    private sealed record Current(Guid? UserId) : ICurrentUser;
    private sealed class FailureFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Use((context, nextMiddleware) => context.Request.Path == "/__test/failure"
                ? throw new InvalidOperationException("secret-test-password") : nextMiddleware(context));
        };
    }
}
