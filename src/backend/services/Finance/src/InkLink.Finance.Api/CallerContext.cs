using InkLink.Finance.Application.Accounts;
using Microsoft.AspNetCore.Authentication;

namespace InkLink.Finance.Api;

internal sealed class FinanceCaller : ICurrentFinanceAccount
{
    public FinanceAccount? Account { get; private set; }
    public string Credential { get; private set; } = "";
    public void Set(FinanceAccount account, string credential) { Account = account; Credential = credential; }
}

internal sealed class FinanceAccountEndpoint;
internal sealed class FinanceCallerMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, FinanceCaller caller, ResolveFinanceAccount resolve)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<FinanceAccountEndpoint>() is not null && context.User.Identity?.IsAuthenticated == true)
        {
            context.Response.Headers.CacheControl = "no-store";
            var token = await context.GetTokenAsync("access_token");
            caller.Set(await resolve.ExecuteAsync(token ?? string.Empty, context.RequestAborted), token ?? string.Empty);
        }
        await next(context);
    }
}
