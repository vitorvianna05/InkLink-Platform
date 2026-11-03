using InkLink.Finance.Application.Accounts;
using InkLink.Finance.Application.Ledger;
namespace InkLink.Finance.Api.Extensions;

internal static class FinanceUseCases
{
    public static IServiceCollection AddFinanceUseCases(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ResolveFinanceAccount>();
        services.AddScoped<ManageLedger>();
        return services;
    }
}
