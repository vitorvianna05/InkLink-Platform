using InkLink.Engagements.Application.Accounts;
using InkLink.Engagements.Infrastructure.Integration;

namespace InkLink.Engagements.Api.Extensions;

internal static class IdentityIntegrationExtensions
{
    public static IServiceCollection AddIdentityIntegration(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var address = configuration["Engagements:IdentityAddress"];
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")
            || (uri.Scheme != "https" && !environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("Engagements requires an Identity address; HTTPS is mandatory outside local/test environments.");
        services.AddHttpClient<IIdentityDirectory, IdentityDirectory>(http => { http.BaseAddress = uri; http.Timeout = TimeSpan.FromSeconds(10); })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        return services;
    }
}
