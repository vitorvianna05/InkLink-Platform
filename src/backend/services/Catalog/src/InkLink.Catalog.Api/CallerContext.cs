using InkLink.Catalog.Application.Accounts;
using Microsoft.AspNetCore.Authentication;

namespace InkLink.Catalog.Api;

internal sealed class CatalogCaller : ICurrentCatalogAccount
{
    public CatalogAccount? Account { get; private set; }
    public void Set(CatalogAccount account) => Account = account;
}

internal sealed class CatalogAccountEndpoint;
internal sealed class CatalogCallerMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, CatalogCaller caller, ResolveCatalogAccount resolve)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<CatalogAccountEndpoint>() is not null && context.User.Identity?.IsAuthenticated == true)
        {
            context.Response.Headers.CacheControl = "no-store";
            var token = await context.GetTokenAsync("access_token");
            caller.Set(await resolve.ExecuteAsync(token ?? string.Empty, context.RequestAborted));
        }
        await next(context);
    }
}
