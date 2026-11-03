using InkLink.Scheduling.Application.Accounts;
using Microsoft.AspNetCore.Authentication;

namespace InkLink.Scheduling.Api;

internal sealed class SchedulingCaller : ICurrentSchedulingAccount
{
    public SchedulingAccount? Account { get; private set; }
    public void Set(SchedulingAccount account) => Account = account;
}

internal sealed class SchedulingAccountEndpoint;
internal sealed class SchedulingCallerMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, SchedulingCaller caller, ResolveSchedulingAccount resolve)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<SchedulingAccountEndpoint>() is not null && context.User.Identity?.IsAuthenticated == true)
        {
            context.Response.Headers.CacheControl = "no-store";
            var token = await context.GetTokenAsync("access_token");
            caller.Set(await resolve.ExecuteAsync(token ?? string.Empty, context.RequestAborted));
        }
        await next(context);
    }
}
