using InkLink.Finance.Application.Accounts;
using InkLink.Finance.Infrastructure.Integration;

namespace InkLink.Finance.Api.Extensions;

internal static class IdentityIntegrationExtensions
{
    public static IServiceCollection AddIdentityIntegration(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var address = configuration["Finance:IdentityAddress"];
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")
            || (uri.Scheme != "https" && !environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("Finance requires an Identity address; HTTPS is mandatory outside local/test environments.");
        services.AddHttpClient<IIdentityDirectory, IdentityDirectory>(http => { http.BaseAddress = uri; http.Timeout = TimeSpan.FromSeconds(10); })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false });
        return services;
    }
}
