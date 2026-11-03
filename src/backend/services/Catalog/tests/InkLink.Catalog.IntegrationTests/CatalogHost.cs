using InkLink.Catalog.Application.Accounts;
using InkLink.Catalog.Infrastructure.Integration;
using InkLink.Identity.IntegrationTests;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace InkLink.Catalog.IntegrationTests;

internal sealed class CatalogHost(CatalogFixture fixture, string connection, bool identityUnavailable = false) : WebApplicationFactory<CatalogProgram>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Testing")
        .UseSecuritySettings(SecurityConfiguration.Identity(fixture.Security).Concat(new Dictionary<string, string?>
        {
            ["CatalogDatabase:ConnectionString"] = connection,
            ["Catalog:IdentityAddress"] = "http://identity.test/"
        }))
        .ConfigureTestServices(services =>
        {
            services.Configure<JwtBearerOptions>("Bearer", options => options.BackchannelHttpHandler = fixture.Security.CreateProviderHandler());
            services.AddHttpClient<IIdentityDirectory, IdentityDirectory>().ConfigurePrimaryHttpMessageHandler(() => identityUnavailable ? new UnavailableIdentityHandler() : fixture.Identity.Server.CreateHandler());
        });
}


internal sealed class UnavailableIdentityHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));
}
