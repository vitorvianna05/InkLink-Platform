using System.Security.Claims;
using InkLink.Authentication;
using InkLink.Identity.Application;
using InkLink.Identity.Application.Registration;

namespace InkLink.Identity.Api;

internal sealed class CallerContext : ICurrentUser, ICurrentContact
{
    public Guid? UserId { get; private set; }
    public bool IsArtist { get; private set; }
    /// <summary>Conta do cadastro ainda sem confirmação (decisão 0020).</summary>
    public bool RegistrationPending { get; private set; }
    public string? Email { get; private set; }
    public void Set(AccountIdentity identity, string? email = null)
    {
        UserId = identity.ProfileId;
        IsArtist = identity.IsArtist;
        RegistrationPending = identity.RegistrationPending;
        Email = email;
    }
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
            // A página de cadastro do Keycloak cria contas com email_verified falso; contas administrativas chegam
            // verificadas e a ausência da claim não torna a conta pendente (decisão 0020).
            var selfRegistered = string.Equals(context.User.FindFirstValue("email_verified"), "false", StringComparison.OrdinalIgnoreCase);
            caller.Set(await provision.ExecuteAsync(issuer, subject, name, context.RequestAborted, selfRegistered), context.User.FindFirstValue("email"));
        }
        await next(context);
    }
}

internal sealed record RenameProfileRequest(string? DisplayName);
internal sealed record MyProfileResponse(Guid Id, string DisplayName, string[] Roles, string? PhotoUrl = null, string? PhotoThumbnailUrl = null);
internal sealed record PhotoResponse(string PhotoUrl, string PhotoThumbnailUrl);

// Routing selects this marker for every supported spelling of the own-profile endpoints.
internal sealed class CurrentAccountEndpoint;

/// <summary>Recusa a conta pendente nas rotas do próprio perfil: só a confirmação do cadastro fica disponível.</summary>
internal sealed class ConfirmedAccountFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var caller = context.HttpContext.RequestServices.GetRequiredService<CallerContext>();
        if (caller.RegistrationPending) return ConfirmationRequired();
        return await next(context);
    }

    public static IResult ConfirmationRequired() => Results.Problem(statusCode: StatusCodes.Status403Forbidden,
        title: "Confirm the registration before using the account.", extensions: new Dictionary<string, object?> { ["code"] = "account.confirmation_required" });
}
