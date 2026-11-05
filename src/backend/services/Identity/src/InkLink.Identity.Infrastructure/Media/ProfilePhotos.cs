using System.Diagnostics;
using System.Text.Json;
using Amazon.Runtime;
using InkLink.Identity.Application;
using InkLink.Identity.Domain;
using InkLink.Imaging;
using InkLink.ObjectStorage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InkLink.Identity.Infrastructure.Media;

/// <summary>Fotos de perfil no armazenamento S3 (decisão 0018).</summary>
public sealed class ProfilePhotos(IdentityDbContext database, IObjectStorage storage, TimeProvider time, ILogger<ProfilePhotos> logger) : IProfilePhotos
{
    public static readonly TimeSpan ReservationWindow = TimeSpan.FromHours(1);

    public IReadOnlyList<string> Objects(string key) => ImageRules.ProfilePhoto.Variants.Select(size => $"{key}/{size}.webp").ToArray();

    public async Task<string> StoreAsync(Guid profileId, byte[] content, bool isPublic, CancellationToken cancellationToken)
    {
        IReadOnlyList<ImageVariant> variants;
        try { variants = ImageNormalizer.Normalize(content, ImageRules.ProfilePhoto); }
        catch (InvalidImageException invalid) { throw new InvalidPhoto(invalid.Message); }
        var key = $"profiles/{profileId:N}/{Guid.CreateVersion7():N}";
        var reservations = Objects(key).Select(item => new ObjectCleanup { Key = item, Private = !isPublic, DueAt = time.GetUtcNow().Add(ReservationWindow) }).ToArray();
        database.ObjectCleanups.AddRange(reservations);
        await database.SaveChangesAsync(cancellationToken);
        foreach (var reservation in reservations) database.Entry(reservation).State = EntityState.Detached;
        var visibility = isPublic ? ObjectVisibility.Public : ObjectVisibility.Private;
        try
        {
            foreach (var variant in variants)
                await storage.PutAsync(visibility, $"{key}/{variant.FileName}", new MemoryStream(variant.Content), ImageVariant.ContentType, cancellationToken);
        }
        catch (Exception exception) when (exception is AmazonServiceException or AmazonClientException or HttpRequestException)
        {
            logger.LogWarning("Profile photo upload failed; reserved objects are left for cleanup.");
            throw new PhotoStorageUnavailable();
        }
        return key;
    }

    public async Task<PhotoUrls?> ResolveAsync(string? key, bool isPublic, CancellationToken cancellationToken)
    {
        if (key is null) return null;
        var large = $"{key}/{ImageRules.ProfilePhoto.Variants[0]}.webp";
        var small = $"{key}/{ImageRules.ProfilePhoto.Variants[^1]}.webp";
        if (isPublic) return new(storage.GetPublicUrl(large).AbsoluteUri, storage.GetPublicUrl(small).AbsoluteUri);
        return new((await storage.CreateSignedReadUrlAsync(large, cancellationToken)).AbsoluteUri, (await storage.CreateSignedReadUrlAsync(small, cancellationToken)).AbsoluteUri);
    }
}

/// <summary>Sem armazenamento configurado (somente testes isolados): sem foto e envio indisponível.</summary>
public sealed class DisabledProfilePhotos : IProfilePhotos
{
    public Task<string> StoreAsync(Guid profileId, byte[] content, bool isPublic, CancellationToken cancellationToken) => throw new PhotoStorageUnavailable();
    public IReadOnlyList<string> Objects(string key) => [];
    public Task<PhotoUrls?> ResolveAsync(string? key, bool isPublic, CancellationToken cancellationToken) => Task.FromResult<PhotoUrls?>(null);
}

/// <summary>Gravação da foto: perfil, confirmação, remoções agendadas e evento na mesma transação.</summary>
internal sealed class EfPhotoStore(IdentityDbContext database, TimeProvider time) : IPhotoStore
{
    public const string EventType = "identity.profile-photo-changed.v1";

    public async Task SavePhotoAsync(Profile profile, PhotoChange change, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var now = time.GetUtcNow();
        if (change.Event is { } photo)
        {
            var id = Guid.CreateVersion7();
            database.Outbox.Add(new OutboxEvent
            {
                Id = id,
                Type = EventType,
                CreatedAt = now,
                Payload = JsonSerializer.Serialize(new
                {
                    messageId = id,
                    type = EventType,
                    schemaVersion = 1,
                    occurredAt = now,
                    correlationId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N"),
                    traceParent = Activity.Current?.Id,
                    causationId = (string?)null,
                    data = new { profileId = photo.ProfileId, revision = photo.Revision, photoUrl = photo.PhotoUrl, photoThumbnailUrl = photo.PhotoThumbnailUrl },
                }),
            });
        }
        await database.SaveChangesAsync(cancellationToken);
        if (change.Confirmed.Count > 0)
            await database.ObjectCleanups.Where(item => change.Confirmed.Contains(item.Key)).ExecuteDeleteAsync(cancellationToken);
        foreach (var item in change.Obsolete)
        {
            var isPrivate = !item.Public;
            await database.Database.ExecuteSqlAsync(
                $"INSERT INTO identity.object_cleanups (\"Id\", \"Key\", \"Private\", \"DueAt\", \"Attempts\") VALUES ({Guid.CreateVersion7()}, {item.Key}, {isPrivate}, {now}, 0) ON CONFLICT (\"Key\") DO UPDATE SET \"DueAt\" = EXCLUDED.\"DueAt\"",
                cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }
}
