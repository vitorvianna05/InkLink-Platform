using System.Diagnostics;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics;

namespace InkLink.Messaging.Api.Extensions;

internal static class MessagingHttpExtensions
{
    public static IServiceCollection AddMessagingHttpContracts(this IServiceCollection services)
    {
        services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
        services.ConfigureHttpJsonOptions(options => options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);
        services.AddSingleton<IProblemDetailsWriter, ApiProblemDetailsWriter>();
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            context.ProblemDetails.Extensions["traceId"] = Activity.Current?.TraceId.ToString() ?? context.HttpContext.TraceIdentifier;
            context.ProblemDetails.Extensions.TryAdd("code", "http." + context.ProblemDetails.Status);
        });
        services.AddExceptionHandler<MessagingExceptionHandler>();
        return services;
    }

    public static WebApplication UseMessagingHttpErrors(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        return app;
    }
}
