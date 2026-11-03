using InkLink.Engagements.Application.Negotiations;
using InkLink.Engagements.Infrastructure.Integration;

namespace InkLink.Engagements.Api.Extensions;

internal static class CatalogIntegrationExtensions
{
    public static IServiceCollection AddCatalogIntegration(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var address = Address(configuration["Engagements:CatalogAddress"], environment);
        var authority = Address(configuration["Authentication:Authority"], environment);
        var secret = configuration["Engagements:ClientSecret"];
        if (string.IsNullOrWhiteSpace(secret)) throw new InvalidOperationException("Engagements:ClientSecret is required from secure configuration.");
        services.AddSingleton(new CatalogCredentials(new Uri(authority, authority.AbsolutePath.TrimEnd('/') + "/protocol/openid-connect/token"), "inklink-engagements", secret));
        services.AddHttpClient<ServiceTokenClient>(http => http.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        services.AddHttpClient<ICatalogDirectory, CatalogDirectory>(http => { http.BaseAddress = address; http.Timeout = TimeSpan.FromSeconds(10); })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        return services;
    }

    private static Uri Address(string? value, IHostEnvironment environment)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")
            || (uri.Scheme != "https" && !environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("Engagements requires a valid backend address; HTTPS is mandatory outside local/test environments.");
        return uri;
    }
}
