using Amazon.Runtime;
using InkLink.Catalog.Application;
using InkLink.Catalog.Application.Publishing;
using InkLink.Catalog.Domain;
using InkLink.Catalog.Infrastructure.Persistence;
using InkLink.Imaging;
using InkLink.ObjectStorage;
using Microsoft.Extensions.Logging;

namespace InkLink.Catalog.Infrastructure.Media;

/// <summary>Imagens de obras no armazenamento S3 (decisão 0018): variantes WebP públicas e imutáveis.</summary>
public sealed class ArtworkImages(CatalogDbContext database, IObjectStorage storage, TimeProvider time, ILogger<ArtworkImages> logger) : IArtworkImages
{
    /// <summary>Prazo da reserva: se a obra não for gravada até lá, o worker remove os objetos enviados.</summary>
    public static readonly TimeSpan ReservationWindow = TimeSpan.FromHours(1);

    public IReadOnlyList<string> Objects(string imageKey) => ImageRules.Artwork.Variants.Select(size => $"{imageKey}/{size}.webp").ToArray();

    public async Task<string> StoreAsync(Guid artistId, byte[] content, CancellationToken cancellationToken)
    {
        IReadOnlyList<ImageVariant> variants;
        try { variants = ImageNormalizer.Normalize(content, ImageRules.Artwork); }
        catch (InvalidImageException invalid) { throw new InvalidCatalogData("image", invalid.Message); }

        var key = $"artworks/{artistId:N}/{Guid.CreateVersion7():N}";
        var objects = Objects(key);
        var reservations = objects.Select(item => new ObjectCleanup { Key = item, DueAt = time.GetUtcNow().Add(ReservationWindow) }).ToArray();
        database.ObjectCleanups.AddRange(reservations);
        await database.SaveChangesAsync(cancellationToken);
        foreach (var reservation in reservations) database.Entry(reservation).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
        try
        {
            foreach (var variant in variants)
                await storage.PutAsync(ObjectVisibility.Public, $"{key}/{variant.FileName}", new MemoryStream(variant.Content), ImageVariant.ContentType, cancellationToken);
        }
        catch (Exception exception) when (exception is AmazonServiceException or AmazonClientException or HttpRequestException)
        {
            logger.LogWarning("Artwork image upload failed; reserved objects are left for cleanup.");
            throw new CatalogFailure(CatalogError.Unavailable, "catalog.storage_unavailable");
        }
        return key;
    }
}

/// <summary>
/// Endereços de leitura das obras: variantes enviadas ou URL externa legada. Sem armazenamento configurado
/// (somente em testes de outros serviços que hospedam o Catálogo), não há obras enviadas para resolver.
/// </summary>
public sealed class ArtworkImageUrls(IObjectStorage? storage)
{
    public (string Detail, string Thumbnail, bool External) Resolve(string? imageKey, string? legacyUrl)
    {
        if (imageKey is null) return (legacyUrl ?? string.Empty, legacyUrl ?? string.Empty, true);
        if (storage is null) throw new InvalidOperationException("Object storage is not configured.");
        return (storage.GetPublicUrl($"{imageKey}/{ImageRules.Artwork.Variants[0]}.webp").AbsoluteUri,
            storage.GetPublicUrl($"{imageKey}/{ImageRules.Artwork.Variants[^1]}.webp").AbsoluteUri, false);
    }
}

/// <summary>Envio indisponível quando o ambiente de teste não configura o armazenamento: responde 503.</summary>
public sealed class DisabledArtworkImages : IArtworkImages
{
    public IReadOnlyList<string> Objects(string imageKey) => ImageRules.Artwork.Variants.Select(size => $"{imageKey}/{size}.webp").ToArray();
    public Task<string> StoreAsync(Guid artistId, byte[] content, CancellationToken cancellationToken) =>
        throw new CatalogFailure(CatalogError.Unavailable, "catalog.storage_unavailable");
}
