using InkLink.Scheduling.Application.Reservations;
using InkLink.Scheduling.Infrastructure.Integration;

namespace InkLink.Scheduling.Api.Extensions;

internal static class SchedulingIntegrationExtensions
{
    public static IServiceCollection AddSchedulingIntegrations(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        Uri Address(string key)
        {
            var value = configuration[key];
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")
                || (uri.Scheme != "https" && !environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
                || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                throw new InvalidOperationException("Scheduling requires valid integration URLs; HTTPS outside local/test.");
            return uri;
        }
        var identity = Address("Scheduling:IdentityAddress"); var engagements = Address("Scheduling:EngagementsAddress");
        var authority = Address("Authentication:Authority");
        var secret = configuration["Scheduling:ClientSecret"];
        if (string.IsNullOrWhiteSpace(secret)) throw new InvalidOperationException("Scheduling credential required.");
        services.AddSingleton(new SchedulingCredentials(new Uri(authority.ToString().TrimEnd('/') + "/protocol/openid-connect/token"), secret));
        services.AddScoped<TechnicalTokens>();
        services.AddHttpClient<SchedulingTokenClient>(c => c.Timeout = TimeSpan.FromSeconds(10)).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        services.AddHttpClient<IEngagementDirectory, EngagementDirectory>(c => { c.BaseAddress = engagements; c.Timeout = TimeSpan.FromSeconds(10); }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        services.AddHttpClient<IParticipantDirectory, ParticipantDirectory>(c => { c.BaseAddress = identity; c.Timeout = TimeSpan.FromSeconds(10); }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        return services;
    }
}
