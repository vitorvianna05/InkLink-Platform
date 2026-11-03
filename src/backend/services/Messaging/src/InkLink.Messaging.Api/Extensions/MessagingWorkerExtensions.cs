namespace InkLink.Messaging.Api.Extensions;

internal static class MessagingWorkerExtensions
{
    public static IServiceCollection AddConversationConsumer(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        if (!environment.IsEnvironment("Testing") || configuration.GetValue<bool>("Broker:EnableWorkers")) services.AddHostedService<ConversationConsumer>();
        return services;
    }
}
