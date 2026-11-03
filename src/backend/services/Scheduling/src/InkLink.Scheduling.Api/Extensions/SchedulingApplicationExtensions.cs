using InkLink.Scheduling.Application.Accounts;
using InkLink.Scheduling.Application.Reservations;

namespace InkLink.Scheduling.Api.Extensions;

internal static class SchedulingApplicationExtensions
{
    public static IServiceCollection AddSchedulingUseCases(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ResolveSchedulingAccount>();
        services.AddScoped<ManageSchedule>();
        return services;
    }
}
