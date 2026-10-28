using System.Security.Claims;
using InkLink.Authentication;
using InkLink.Identity.Application;

namespace InkLink.Identity.Api;

internal sealed class CallerContext : ICurrentUser
{
    public Guid? UserId { get; private set; }
    public bool IsArtist { get; private set; }
    public void Set(AccountIdentity identity) { UserId = identity.ProfileId; IsArtist = identity.IsArtist; }
}

internal sealed class CallerMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, CallerContext caller, ProvisionAccount provision, AccessTokenSettings settings)
    {
        if (AccessTokens.IsUser(context.User, settings) && context.GetEndpoint()?.Metadata.GetMetadata<CurrentAccountEndpoint>() is not null)
        {
            var issuer = context.User.FindFirstValue("iss")!;
            var subject = context.User.FindFirstValue("sub")!;
            var name = context.User.FindFirstValue("name") ?? context.User.FindFirstValue("preferred_username") ?? "Cliente";
            caller.Set(await provision.ExecuteAsync(issuer, subject, name, context.RequestAborted));
        }
        await next(context);
    }
}

internal sealed record RenameProfileRequest(string? DisplayName);
internal sealed record MyProfileResponse(Guid Id, string DisplayName, string[] Roles);

// Routing selects this marker for every supported spelling of the own-profile endpoints.
internal sealed class CurrentAccountEndpoint;
