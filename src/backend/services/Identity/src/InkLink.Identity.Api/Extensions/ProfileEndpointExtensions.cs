using InkLink.Authentication;
using InkLink.Identity.Application;
using Microsoft.AspNetCore.Mvc;

namespace InkLink.Identity.Api.Extensions;

internal static class ProfileEndpointExtensions
{
    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/profiles/me", async (GetMyProfile query, CallerContext caller, CancellationToken cancellationToken) =>
        {
            var result = await query.ExecuteAsync(cancellationToken);
            return result.Status == ProfileStatus.Found
                ? Results.Ok(new MyProfileResponse(result.Profile!.Id, result.Profile.DisplayName, caller.IsArtist ? ["Client", "Artist"] : ["Client"], result.Profile.PhotoUrl, result.Profile.PhotoThumbnailUrl))
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
        // Foto de perfil por upload (decisão 0018): uma por conta; JPEG, PNG ou WebP até 5 MB.
        app.MapPut("/api/v1/profiles/me/photo", async (HttpRequest request, ChangeMyPhoto command, CancellationToken cancellationToken) =>
        {
            if (!request.HasFormContentType) return Results.Problem(statusCode: 415);
            var form = await request.ReadFormAsync(cancellationToken);
            if (form.Keys.Count > 0 || form.Files.Count != 1 || form.Files[0].Name != "image")
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["image"] = ["Send exactly one file named image."] });
            var file = form.Files[0];
            if (file.Length > InkLink.Imaging.ImageRules.ProfilePhoto.MaxBytes)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["image"] = ["The image must have at most 5 MB."] });
            using var buffer = new MemoryStream((int)file.Length);
            await using (var stream = file.OpenReadStream()) await stream.CopyToAsync(buffer, cancellationToken);
            try
            {
                var result = await command.UploadAsync(buffer.ToArray(), cancellationToken);
                return result.Status switch
                {
                    PhotoStatus.Updated => Results.Ok(new PhotoResponse(result.Photo!.Url, result.Photo.ThumbnailUrl)),
                    PhotoStatus.Unauthenticated => Results.Problem(statusCode: 401),
                    _ => Results.Problem(statusCode: 404),
                };
            }
            catch (InvalidPhoto invalid) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["image"] = [invalid.Message] }); }
            catch (PhotoStorageUnavailable) { return Results.Problem(statusCode: 503, extensions: new Dictionary<string, object?> { ["code"] = "identity.storage_unavailable" }); }
        }).RequireAuthorization(AccessTokens.UserPolicy).WithMetadata(new CurrentAccountEndpoint(),
                new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(5 * 1024 * 1024 + 16 * 1024))
            .Accepts<PhotoForm>("multipart/form-data").Produces<PhotoResponse>().ProducesValidationProblem().ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(415).ProducesProblem(503).ProducesProblem(StatusCodes.Status500InternalServerError);
        app.MapDelete("/api/v1/profiles/me/photo", async (ChangeMyPhoto command, CancellationToken cancellationToken) =>
        {
            var result = await command.RemoveAsync(cancellationToken);
            return result.Status switch
            {
                PhotoStatus.Removed => Results.NoContent(),
                PhotoStatus.Unauthenticated => Results.Problem(statusCode: 401),
                _ => Results.Problem(statusCode: 404),
            };
        }).RequireAuthorization(AccessTokens.UserPolicy).WithMetadata(new CurrentAccountEndpoint()).Produces(204).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(StatusCodes.Status500InternalServerError);
        // A foto só é resolvida quando o chamador a pede (include=photo): hoje apenas Messaging, depois de validar a
        // participação na conversa. Foto privada sai assinada e com validade curta; os demais chamadores recebem só o nome.
        app.MapGet("/api/v1/internal/profiles/{id:guid}", async (Guid id, [FromQuery] string? include, IProfileReader profiles, IProfilePhotos photos, CancellationToken cancellationToken) =>
        {
            var profile = await profiles.FindByIdAsync(id, cancellationToken);
            if (profile is null) return Results.NotFound();
            var photo = include == "photo" ? await photos.ResolveAsync(profile.PhotoKey, profile.PhotoPublic, cancellationToken) : null;
            return Results.Ok(new ProfileResponse(profile.Id, profile.DisplayName, photo?.Url, photo?.ThumbnailUrl));
        }).RequireAuthorization(AccessTokens.TechnicalPolicy).Produces<ProfileResponse>().ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(StatusCodes.Status500InternalServerError);
        return app;
    }
}

/// <summary>Formulário multipart da foto de perfil, documentado no OpenAPI.</summary>
internal sealed record PhotoForm(IFormFile Image);
