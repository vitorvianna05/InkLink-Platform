using InkLink.Engagements.Application.Lifecycle;
using InkLink.Engagements.Infrastructure;
using InkLink.Engagements.Infrastructure.Integration;
using InkLink.Engagements.Infrastructure.Persistence;
namespace InkLink.Engagements.Api.Extensions;

internal static class LifecycleExtensions
{
    public static IServiceCollection AddEngagementLifecycle(this IServiceCollection services, IConfiguration config, IHostEnvironment environment)
    {
        services.AddScoped<ILifecycleStore, EfLifecycleStore>();
        services.AddScoped<ManageLifecycle>();
        services.AddScoped<CoordinateLifecycle>();
        var scheduling = config["Engagements:SchedulingAddress"];
        var messaging = config["Engagements:MessagingAddress"];
        if (scheduling is null && messaging is null && !environment.IsEnvironment("Testing"))
            throw new InvalidOperationException("Engagements lifecycle backend addresses are required.");
        if (scheduling is not null || messaging is not null)
        {
            services.AddSingleton(new LifecycleAddresses(Address(scheduling, environment), Address(messaging, environment)));
            services.AddHttpClient<ILifecycleDirectory, LifecycleDirectory>(http => http.Timeout = TimeSpan.FromSeconds(10))
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false });
            if (config.GetValue<bool?>("Lifecycle:EnableWorkers") ?? !environment.IsEnvironment("Testing"))
                services.AddHostedService<LifecycleWorker>();
        }
        return services;
    }
    private static Uri Address(string? value, IHostEnvironment environment)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")
            || (uri.Scheme != "https" && !environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidOperationException("Lifecycle requires backend addresses; HTTPS is mandatory outside local/test.");
        return uri;
    }
    public static IEndpointRouteBuilder MapEngagementLifecycle(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v2/engagements").WithTags("Engagement lifecycle");
        group.MapPost("/{id:guid}/closure", async (Guid id, CloseInput input, ManageLifecycle command, HttpContext http, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            var op = await command.CloseAsync(id, input.Confirmed, ct);
            return op.Status == "Completed" ? Results.Ok(op) : Results.Accepted($"/api/v2/engagements/{id}/lifecycle", op);
        }).RequireEngagementsAccount().Produces<LifecycleView>(202).Produces<LifecycleView>().WithLifecycleErrors();
        group.MapGet("/{id:guid}/lifecycle", (Guid id, ManageLifecycle query, HttpContext http, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store"; return query.ReadAsync(id, ct);
        }).RequireEngagementsAccount().Produces<LifecycleStatus>().WithLifecycleErrors();
        group.MapPost("/{id:guid}/lifecycle/retry", async (Guid id, ManageLifecycle command, CancellationToken ct) =>
        { await command.RetryAsync(id, ct); return Results.Accepted($"/api/v2/engagements/{id}/lifecycle"); })
        .RequireEngagementsAccount().Produces(202).WithLifecycleErrors();
        return endpoints;
    }
    private static RouteHandlerBuilder WithLifecycleErrors(this RouteHandlerBuilder endpoint) => endpoint.ProducesValidationProblem()
        .ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409).ProducesProblem(500).ProducesProblem(503);
    private sealed record CloseInput(bool Confirmed);
}
