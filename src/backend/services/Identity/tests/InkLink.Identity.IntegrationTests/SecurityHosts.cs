using InkLink.Bff;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Forwarder;

namespace InkLink.Identity.IntegrationTests;

internal sealed class SecurityIdentityHost(SecurityFixture fixture) : WebApplicationFactory<IdentityProgram>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Testing")
        .UseSecuritySettings(SecurityConfiguration.Identity(fixture))
        .ConfigureTestServices(services => services.Configure<JwtBearerOptions>("Bearer", options => options.BackchannelHttpHandler = fixture.CreateProviderHandler()));
}

internal sealed class SecurityGatewayHost(SecurityFixture fixture, HttpMessageHandler upstream, RecordedLogs? logs = null, bool failRoute = false) : WebApplicationFactory<GatewayProgram>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Testing")
        .UseSecuritySettings(SecurityConfiguration.Identity(fixture)
            .Concat(new Dictionary<string, string?> { ["Gateway:IdentityAddress"] = "http://identity.test/" }))
        .ConfigureTestServices(services =>
        {
            services.AddSingleton<IForwarderHttpClientFactory>(new ProxyClientFactory(upstream));
            if (logs is not null) services.AddSingleton<ILoggerProvider>(logs);
            if (failRoute) services.AddTransient<IStartupFilter, TestFailureFilter>();
            services.Configure<JwtBearerOptions>("Bearer", options => options.BackchannelHttpHandler = fixture.CreateProviderHandler());
        });
}

internal sealed class SecurityBffHost(SecurityFixture fixture, HttpMessageHandler upstream, RecordedLogs? logs = null, bool failRoute = false) : WebApplicationFactory<BffProgram>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Testing")
        .UseSecuritySettings(new Dictionary<string, string?>
        {
            ["Bff:Authority"] = fixture.Authority,
            ["Bff:ClientId"] = "inklink-bff",
            ["Bff:ClientSecret"] = fixture.BffSecret,
            ["Bff:GatewayAddress"] = "http://gateway.test/",
            ["Bff:ConnectionString"] = fixture.BffConnection,
            ["Bff:CertificatePath"] = fixture.CertificatePath,
            ["Bff:CertificatePassword"] = fixture.CertificatePassword,
            ["Bff:AllowHttpMetadata"] = "false"
        })
        .ConfigureTestServices(services =>
        {
            services.AddSingleton<IForwarderHttpClientFactory>(new ProxyClientFactory(upstream));
            if (logs is not null) services.AddSingleton<ILoggerProvider>(logs);
            if (failRoute) services.AddTransient<IStartupFilter, TestFailureFilter>();
            services.Configure<OpenIdConnectOptions>("oidc", options => options.BackchannelHttpHandler = fixture.CreateProviderHandler());
        });
}

internal sealed class ProxyClientFactory(HttpMessageHandler handler) : IForwarderHttpClientFactory
{
    public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) => new(handler, disposeHandler: false);
}

internal static class SecurityConfiguration
{
    public static Dictionary<string, string?> Identity(SecurityFixture fixture) => new()
    {
        ["IdentityDatabase:ConnectionString"] = fixture.Database.AppConnection,
        ["Authentication:Authority"] = fixture.Authority,
        ["Authentication:Audience"] = "inklink-api",
        ["Authentication:AllowHttpMetadata"] = "false",
        ["Authentication:UserClients:0"] = "inklink-bff",
        ["Authentication:UserClients:1"] = "inklink-test-users",
        ["Authentication:UserClients:2"] = "inklink-test-short",
        ["Authentication:UserClients:3"] = "inklink-test-other-audience",
        ["Authentication:TechnicalClients:0"] = "inklink-catalog"
    };
}

internal static class HostSettings
{
    public static IWebHostBuilder UseSecuritySettings(this IWebHostBuilder builder, IEnumerable<KeyValuePair<string, string?>> settings)
    {
        foreach (var (key, value) in settings) builder.UseSetting(key, value);
        return builder;
    }
}

internal sealed class TestFailureFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        next(app);
        app.Use((context, nextMiddleware) => context.Request.Path == "/__tests/failure"
            ? throw new InvalidOperationException("private-failure-marker") : nextMiddleware(context));
    };
}
