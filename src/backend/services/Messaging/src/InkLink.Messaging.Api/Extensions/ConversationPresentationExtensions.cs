using InkLink.Messaging.Application;
using InkLink.Messaging.Application.Conversations;
using InkLink.Messaging.Infrastructure.Integration;
using Microsoft.AspNetCore.Authentication;

namespace InkLink.Messaging.Api.Extensions;

internal static class ConversationPresentationExtensions
{
    public static IServiceCollection AddConversationPresentation(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var engagements = Address(configuration["Messaging:EngagementsAddress"], environment);
        var identity = Address(configuration["Messaging:IdentityAddress"], environment);
        var authority = Address(configuration["Authentication:Authority"], environment);
        var secret = configuration["Messaging:ClientSecret"];
        if (string.IsNullOrWhiteSpace(secret)) throw new InvalidOperationException("Messaging service credential is required.");
        services.AddSingleton(new ParticipantCredentials(new Uri(authority.ToString().TrimEnd('/') + "/protocol/openid-connect/token"), secret));
        services.AddHttpContextAccessor();
        services.AddTransient<DelegatedConversationToken>();
        services.AddHttpClient<MessagingTokenClient>(http => http.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        services.AddHttpClient<ParticipantProfiles>(http => { http.BaseAddress = identity; http.Timeout = TimeSpan.FromSeconds(10); })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        services.AddHttpClient<IConversationPresentationDirectory, ConversationPresentationDirectory>(http => { http.BaseAddress = engagements; http.Timeout = TimeSpan.FromSeconds(10); })
            .AddHttpMessageHandler<DelegatedConversationToken>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        return services;
    }

    private static Uri Address(string? value, IHostEnvironment environment)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")
            || (uri.Scheme != "https" && !environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("Messaging requires valid integration addresses; HTTPS is mandatory outside local/test environments.");
        return uri;
    }
}

internal sealed class DelegatedConversationToken(IHttpContextAccessor accessor) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = accessor.HttpContext is { } context ? await context.GetTokenAsync("access_token") : null;
        if (string.IsNullOrWhiteSpace(token)) throw new MessagingFailure(MessagingError.Unavailable, "messaging.presentation_unavailable");
        request.Headers.Authorization = new("Bearer", token);
        return await base.SendAsync(request, cancellationToken);
    }
}
