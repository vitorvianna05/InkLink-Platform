using InkLink.Authentication;
using InkLink.Identity.Application;

namespace InkLink.Identity.Api.Extensions;

internal static class ProfileEndpointExtensions
{
    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/profiles/me", async (GetMyProfile query, CallerContext caller, CancellationToken cancellationToken) =>
        {
            var result = await query.ExecuteAsync(cancellationToken);
            return result.Status == ProfileStatus.Found
                ? Results.Ok(new MyProfileResponse(result.Profile!.Id, result.Profile.DisplayName, caller.IsArtist ? ["Client", "Artist"] : ["Client"]))
                : Results.Problem(statusCode: result.Status == ProfileStatus.Unauthenticated ? 401 : 404);
        }).RequireAuthorization(AccessTokens.UserPolicy).WithMetadata(new CurrentAccountEndpoint()).Produces<MyProfileResponse>().ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(StatusCodes.Status500InternalServerError);
        app.MapPut("/api/v1/profiles/me", async (RenameProfileRequest request, UpdateMyProfile command, CancellationToken cancellationToken) =>
        {
            var result = await command.ExecuteAsync(request.DisplayName, cancellationToken);
            return result switch
            {
                UpdateProfileStatus.Updated => Results.NoContent(),
                UpdateProfileStatus.InvalidName => Results.ValidationProblem(new Dictionary<string, string[]> { ["displayName"] = ["Display name is required."] }),
                _ => Results.Problem(statusCode: result == UpdateProfileStatus.Unauthenticated ? 401 : 404)
            };
        }).RequireAuthorization(AccessTokens.UserPolicy).WithMetadata(new CurrentAccountEndpoint()).Produces(204).ProducesValidationProblem().ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(StatusCodes.Status500InternalServerError);
        app.MapGet("/api/v1/internal/profiles/{id:guid}", async (Guid id, IProfileReader profiles, CancellationToken cancellationToken) =>
        {
            var profile = await profiles.FindByIdAsync(id, cancellationToken);
            return profile is null ? Results.NotFound() : Results.Ok(new ProfileResponse(profile.Id, profile.DisplayName));
        }).RequireAuthorization(AccessTokens.TechnicalPolicy).Produces<ProfileResponse>().ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(StatusCodes.Status500InternalServerError);
        return app;
    }
}
