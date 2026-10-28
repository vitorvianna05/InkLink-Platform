using InkLink.Identity.Application;

namespace InkLink.Identity.Api.Extensions;

internal static class IdentityApplicationExtensions
{
    public static IServiceCollection AddIdentityUseCases(this IServiceCollection services)
    {
        services.AddScoped<GetMyProfile>();
        services.AddScoped<UpdateMyProfile>();
        services.AddScoped<ProvisionAccount>();
        return services;
    }
}
