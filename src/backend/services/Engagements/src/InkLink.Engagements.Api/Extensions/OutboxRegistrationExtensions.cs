using InkLink.Engagements.Infrastructure.Messaging;

namespace InkLink.Engagements.Api.Extensions;

internal static class OutboxRegistrationExtensions
{
    public static IServiceCollection AddEngagementsOutbox(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        if (!environment.IsEnvironment("Testing") || configuration.GetValue<bool>("Broker:EnableWorkers")) services.AddHostedService<OutboxWorker>();
        return services;
    }
}
