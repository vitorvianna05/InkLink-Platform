using InkLink.Engagements.Application.Accounts;
using InkLink.Engagements.Application.Negotiations;

namespace InkLink.Engagements.Api.Extensions;

internal static class EngagementsApplicationExtensions
{
    public static IServiceCollection AddEngagementsUseCases(this IServiceCollection services)
    {
        services.AddScoped<ManageEngagements>();
        services.AddScoped<ResolveEngagementsAccount>();
        return services;
    }
}
