using InkLink.Engagements.Application.Scheduling;

namespace InkLink.Engagements.Api.Extensions;

internal static class ReservationWorkerExtensions
{
    public static IServiceCollection AddReservationConsumer(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddScoped<RecordReservation>();
        if ((!environment.IsEnvironment("Testing") || configuration.GetValue<bool>("Broker:EnableReservationConsumer"))
            && !string.IsNullOrWhiteSpace(configuration["Broker:ReservationConsumerUri"]))
            services.AddHostedService<ReservationConsumer>();
        return services;
    }
}
