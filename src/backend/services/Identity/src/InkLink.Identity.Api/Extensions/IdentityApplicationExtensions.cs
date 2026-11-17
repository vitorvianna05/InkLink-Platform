using InkLink.Identity.Application;
using InkLink.Identity.Application.Registration;

namespace InkLink.Identity.Api.Extensions;

internal static class IdentityApplicationExtensions
{
    public static IServiceCollection AddIdentityUseCases(this IServiceCollection services)
    {
        services.AddScoped<GetMyProfile>();
        services.AddScoped<ChangeMyPhoto>();
        services.AddScoped<UpdateMyProfile>();
        services.AddScoped<ProvisionAccount>();
        services.AddScoped<ManageRegistration>();
        return services;
    }
}
