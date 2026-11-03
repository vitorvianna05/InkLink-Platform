using InkLink.Messaging.Application.Accounts;
using InkLink.Messaging.Application.Conversations;

namespace InkLink.Messaging.Api.Extensions;

internal static class MessagingApplicationExtensions
{
    public static IServiceCollection AddMessagingUseCases(this IServiceCollection services)
    {
        services.AddScoped<ResolveMessagingAccount>();
        services.AddScoped<ManageConversations>();
        services.AddScoped<OpenConversation>();
        return services;
    }
}
