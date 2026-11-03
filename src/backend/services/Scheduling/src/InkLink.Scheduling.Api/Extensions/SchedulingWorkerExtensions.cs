using InkLink.Scheduling.Infrastructure.Messaging;

namespace InkLink.Scheduling.Api.Extensions;

internal static class SchedulingWorkerExtensions
{
    public static IServiceCollection AddSchedulingOutbox(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        if (!environment.IsEnvironment("Testing") || configuration.GetValue<bool>("Broker:EnableWorkers")) services.AddHostedService<OutboxWorker>();
        return services;
    }
}
