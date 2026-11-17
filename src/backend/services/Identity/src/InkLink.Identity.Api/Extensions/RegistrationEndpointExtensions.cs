using System.Globalization;
using InkLink.Authentication;
using InkLink.Identity.Application.Registration;
using InkLink.Identity.Domain;

namespace InkLink.Identity.Api.Extensions;

internal sealed record TermsRequest(string? Version);
internal sealed record PhoneRequest(string? PhoneNumber);
internal sealed record CodeRequest(string? Channel);
internal sealed record VerifyRequest(string? Channel, string? Code);

/// <summary>
/// Confirmação do cadastro (decisão 0020). Disponível para a conta pendente, que não usa as demais APIs privadas.
/// </summary>
internal static class RegistrationEndpointExtensions
{
    public static IEndpointRouteBuilder MapRegistrationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/registration").WithTags("Registration")
            .RequireAuthorization(AccessTokens.UserPolicy).WithMetadata(new CurrentAccountEndpoint())
            .ProducesProblem(401).ProducesProblem(403).ProducesProblem(StatusCodes.Status500InternalServerError);
        group.MapGet("", (ManageRegistration registration, CancellationToken ct) => Run(() => registration.ReadAsync(ct)))
            .Produces<RegistrationView>();
        group.MapPut("/terms", (TermsRequest request, ManageRegistration registration, CancellationToken ct) => Run(() => registration.AcceptTermsAsync(request.Version, ct)))
            .Produces<RegistrationView>().ProducesValidationProblem().ProducesProblem(409);
        group.MapPut("/phone", (PhoneRequest request, ManageRegistration registration, CancellationToken ct) => Run(() => registration.ChangePhoneAsync(request.PhoneNumber, ct)))
            .Produces<RegistrationView>().ProducesValidationProblem();
        group.MapPost("/codes", (CodeRequest request, ManageRegistration registration, CancellationToken ct) =>
                Channel(request.Channel) is { } channel ? Run(() => registration.SendCodeAsync(channel, ct), StatusCodes.Status202Accepted) : Task.FromResult(InvalidChannel()))
            .Produces<RegistrationView>(StatusCodes.Status202Accepted).ProducesValidationProblem().ProducesProblem(409).ProducesProblem(429).ProducesProblem(503);
        group.MapPost("/codes/verify", (VerifyRequest request, ManageRegistration registration, CancellationToken ct) =>
                Channel(request.Channel) is { } channel ? Run(() => registration.VerifyCodeAsync(channel, request.Code, ct)) : Task.FromResult(InvalidChannel()))
            .Produces<RegistrationView>().ProducesValidationProblem().ProducesProblem(429);
        group.MapPost("/artist-request", (ManageRegistration registration, CancellationToken ct) => Run(() => registration.RequestArtistAsync(ct)))
            .Produces<RegistrationView>();
        return app;
    }

    private static ContactChannel? Channel(string? value) => value?.ToLowerInvariant() switch
    {
        "email" => ContactChannel.Email,
        "phone" => ContactChannel.Phone,
        _ => null,
    };

    private static IResult InvalidChannel() => Results.ValidationProblem(new Dictionary<string, string[]> { ["channel"] = ["Use email or phone."] },
        extensions: new Dictionary<string, object?> { ["code"] = "registration.channel_invalid" });

    private static async Task<IResult> Run(Func<Task<RegistrationView>> action, int success = StatusCodes.Status200OK)
    {
        try
        {
            var view = await action();
            return success == StatusCodes.Status200OK ? Results.Ok(view) : Results.Json(view, statusCode: success);
        }
        catch (RegistrationFailure failure)
        {
            var extensions = new Dictionary<string, object?> { ["code"] = failure.Code };
            if (failure.RetryAt is { } retryAt) extensions["retryAt"] = retryAt;
            return failure.Error switch
            {
                RegistrationError.Invalid => Results.ValidationProblem(
                    new Dictionary<string, string[]> { [failure.Field ?? "request"] = [failure.Code] }, extensions: extensions),
                RegistrationError.Unauthenticated => Results.Problem(statusCode: 401, extensions: extensions),
                RegistrationError.Forbidden => Results.Problem(statusCode: 403, extensions: extensions),
                RegistrationError.Conflict => Results.Problem(statusCode: 409, extensions: extensions),
                RegistrationError.TooManyRequests => new RetryAfterResult(failure.RetryAt, Results.Problem(statusCode: 429, extensions: extensions)),
                _ => Results.Problem(statusCode: 503, extensions: extensions),
            };
        }
    }

    /// <summary>429 com Retry-After em segundos, quando o prazo é conhecido.</summary>
    private sealed class RetryAfterResult(DateTimeOffset? retryAt, IResult inner) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            if (retryAt is { } at)
            {
                var seconds = Math.Max(1, (int)Math.Ceiling((at - DateTimeOffset.UtcNow).TotalSeconds));
                httpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
            }
            return inner.ExecuteAsync(httpContext);
        }
    }
}
