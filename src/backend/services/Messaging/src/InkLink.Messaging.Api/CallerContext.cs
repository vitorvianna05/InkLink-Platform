using InkLink.Messaging.Application.Accounts;
using Microsoft.AspNetCore.Authentication;

namespace InkLink.Messaging.Api;

internal sealed class MessagingCaller : ICurrentMessagingAccount
{
    public MessagingAccount? Account { get; private set; }
    public void Set(MessagingAccount account) => Account = account;
}

internal sealed class MessagingAccountEndpoint;
internal sealed class MessagingCallerMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, MessagingCaller caller, ResolveMessagingAccount resolve)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<MessagingAccountEndpoint>() is not null && context.User.Identity?.IsAuthenticated == true)
        {
            context.Response.Headers.CacheControl = "no-store";
            var token = await context.GetTokenAsync("access_token");
            caller.Set(await resolve.ExecuteAsync(token ?? string.Empty, context.RequestAborted));
        }
        await next(context);
    }
}
