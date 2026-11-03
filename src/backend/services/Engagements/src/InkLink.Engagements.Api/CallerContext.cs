using InkLink.Engagements.Application.Accounts;
using Microsoft.AspNetCore.Authentication;

namespace InkLink.Engagements.Api;

internal sealed class EngagementsCaller : ICurrentEngagementsAccount
{
    public EngagementsAccount? Account { get; private set; }
    public void Set(EngagementsAccount account) => Account = account;
}

internal sealed class EngagementsAccountEndpoint;
internal sealed class EngagementsCallerMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, EngagementsCaller caller, ResolveEngagementsAccount resolve)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<EngagementsAccountEndpoint>() is not null && context.User.Identity?.IsAuthenticated == true)
        {
            context.Response.Headers.CacheControl = "no-store";
            var token = await context.GetTokenAsync("access_token");
            caller.Set(await resolve.ExecuteAsync(token ?? string.Empty, context.RequestAborted));
        }
        await next(context);
    }
}
