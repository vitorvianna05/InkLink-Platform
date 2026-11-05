using System.Globalization;
using InkLink.Catalog.Application.Publishing;
using InkLink.Catalog.Application.Queries;
using InkLink.Catalog.Domain;
using InkLink.Imaging;
using Microsoft.AspNetCore.Mvc;

namespace InkLink.Catalog.Api.Extensions;

/// <summary>Formulário multipart de obra, documentado no OpenAPI (decisão 0018).</summary>
public sealed record ArtworkForm(string Title, string AlternativeText, string Style, decimal? ReferencePrice, IFormFile? Image);

internal static class CatalogPublishingEndpointExtensions
{
    // Arquivo de até 10 MB mais os campos de texto do formulário.
    private const long MaxRequestBytes = 10 * 1024 * 1024 + 64 * 1024;
    private static readonly HashSet<string> FormFields = ["title", "alternativeText", "style", "referencePrice"];

    public static IEndpointRouteBuilder MapCatalogPublishing(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("/api/v1/artists/me", async (ArtistInput input, PublishArtist publish, CancellationToken cancellationToken) =>
        {
            var result = await publish.ExecuteAsync(input, cancellationToken);
            return result.Created ? Results.Created("/api/v1/artists/" + result.Profile.Id, result.Profile) : Results.Ok(result.Profile);
        }).WithTags("Publication").RequireCatalogAccount().Produces<ArtistProfile>().Produces<ArtistProfile>(201).WithPublishingErrors();
        foreach (var kind in new[] { ArtworkKind.Tattoo, ArtworkKind.Flash })
        {
            var segment = kind == ArtworkKind.Flash ? "flashes" : "tattoos";
            // v2: a imagem chega como arquivo; as escritas v1 por URL foram retiradas (decisão 0018).
            var route = "/api/v2/artists/me/" + segment;
            endpoints.MapPost(route, async (HttpRequest request, ManageArtworks manage, CancellationToken cancellationToken) =>
            {
                var (input, image) = await ReadFormAsync(request, cancellationToken);
                var result = await manage.CreateAsync(kind, input, image, cancellationToken);
                return Results.Created($"/api/v1/artists/{result.ArtistId}/{segment}/{result.Artwork.Id}", result.Artwork);
            }).WithTags("Publication").RequireCatalogAccount().AcceptsArtworkForm().Produces<ArtworkView>(201).WithPublishingErrors();
            endpoints.MapPut(route + "/{id:guid}", async (Guid id, HttpRequest request, ManageArtworks manage, CancellationToken cancellationToken) =>
            {
                var (input, image) = await ReadFormAsync(request, cancellationToken);
                await manage.UpdateAsync(id, kind, input, image, cancellationToken);
                return Results.NoContent();
            }).WithTags("Publication").RequireCatalogAccount().AcceptsArtworkForm().Produces(204).WithPublishingErrors();
            // Retirada sinalizada como em Atendimentos v1 (spec §6.4): 410 com o caminho de migração, fora do OpenAPI.
            foreach (var (path, method) in new[] { ($"/api/v1/artists/me/{segment}", "POST"), ($"/api/v1/artists/me/{segment}/{{id:guid}}", "PUT") })
            {
                endpoints.MapMethods(path, [method], () => Results.Problem(statusCode: StatusCodes.Status410Gone,
                    title: $"Artwork writes by image URL were retired. Send the image file as multipart/form-data to /api/v2/artists/me/{segment}.",
                    extensions: new Dictionary<string, object?> { ["code"] = "catalog.artwork_url_writes_retired" }))
                    .RequireAuthorization(InkLink.Authentication.AccessTokens.UserPolicy).ExcludeFromDescription();
            }
            foreach (var version in new[] { "v1", "v2" })
            {
                endpoints.MapDelete($"/api/{version}/artists/me/{segment}/{{id:guid}}", async (Guid id, ManageArtworks manage, CancellationToken cancellationToken) =>
                {
                    await manage.DeleteAsync(id, kind, cancellationToken);
                    return Results.NoContent();
                }).WithTags("Publication").RequireCatalogAccount().Produces(204).WithPublishingErrors();
            }
        }
        return endpoints;
    }

    private static RouteHandlerBuilder AcceptsArtworkForm(this RouteHandlerBuilder endpoint) =>
        endpoint.Accepts<ArtworkForm>("multipart/form-data")
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes), new RequestFormLimitsAttribute { MultipartBodyLengthLimit = MaxRequestBytes })
            .ProducesProblem(413).ProducesProblem(415).ProducesProblem(503);

    private static async Task<(ArtworkInput Input, byte[]? Image)> ReadFormAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType || !request.ContentType!.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase))
            throw new BadHttpRequestException("Use multipart/form-data.", StatusCodes.Status415UnsupportedMediaType);
        var form = await request.ReadFormAsync(cancellationToken);
        if (form.Keys.Any(key => !FormFields.Contains(key)) || form.Files.Any(file => file.Name != "image") || form.Files.Count > 1)
            throw new BadHttpRequestException("Request contains unknown fields.");
        decimal? price = null;
        if (form.TryGetValue("referencePrice", out var priceValue) && !string.IsNullOrWhiteSpace(priceValue))
        {
            if (!decimal.TryParse(priceValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
                throw new InvalidCatalogData("referencePrice", "Use a decimal number with a dot separator.");
            price = parsed;
        }
        byte[]? image = null;
        if (form.Files.GetFile("image") is { } file)
        {
            if (file.Length > ImageRules.Artwork.MaxBytes) throw new InvalidCatalogData("image", "The image must have at most 10 MB.");
            await using var stream = file.OpenReadStream();
            using var buffer = new MemoryStream((int)file.Length);
            await stream.CopyToAsync(buffer, cancellationToken);
            image = buffer.ToArray();
        }
        return (new ArtworkInput(form["title"], form["alternativeText"], form["style"], price), image);
    }

    private static RouteHandlerBuilder WithPublishingErrors(this RouteHandlerBuilder endpoint) =>
        endpoint.ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409).WithCatalogErrors();
}
