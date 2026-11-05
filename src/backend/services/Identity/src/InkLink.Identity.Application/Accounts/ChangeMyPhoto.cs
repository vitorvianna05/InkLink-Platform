using InkLink.Identity.Domain;

namespace InkLink.Identity.Application;

/// <summary>Arquivo recusado pela normalização de imagens (formato, tamanho ou dimensões).</summary>
public sealed class InvalidPhoto(string message) : Exception(message);

/// <summary>Armazenamento indisponível; a operação falha sem gravar a foto.</summary>
public sealed class PhotoStorageUnavailable() : Exception("Photo storage is unavailable.");

public sealed record PhotoUrls(string Url, string ThumbnailUrl);

/// <summary>Evento de foto pública para projeções (identity.profile-photo-changed.v1).</summary>
public sealed record ProfilePhotoChanged(Guid ProfileId, int Revision, string? PhotoUrl, string? PhotoThumbnailUrl);

/// <summary>Objetos confirmados, objetos obsoletos e evento a publicar na mesma transação do perfil.</summary>
public sealed record PhotoChange(IReadOnlyList<string> Confirmed, IReadOnlyList<StoredPhoto> Obsolete, ProfilePhotoChanged? Event);

/// <summary>Fotos de perfil (decisão 0018): variantes WebP quadradas, públicas ou privadas.</summary>
public interface IProfilePhotos
{
    /// <summary>Normaliza, reserva a limpeza e envia as variantes; devolve o prefixo.</summary>
    Task<string> StoreAsync(Guid profileId, byte[] content, bool isPublic, CancellationToken cancellationToken);
    IReadOnlyList<string> Objects(string key);
    /// <summary>Endereço público ou assinado de curta duração; nulo quando não há foto.</summary>
    Task<PhotoUrls?> ResolveAsync(string? key, bool isPublic, CancellationToken cancellationToken);
}

public interface IPhotoStore
{
    Task SavePhotoAsync(Profile profile, PhotoChange change, CancellationToken cancellationToken);
}

public enum PhotoStatus { Updated, Removed, Unauthenticated, NotFound }
public sealed record PhotoResult(PhotoStatus Status, PhotoUrls? Photo = null);

public sealed class ChangeMyPhoto(ICurrentUser currentUser, IAccountStore accounts, IPhotoStore store, IProfilePhotos photos)
{
    public async Task<PhotoResult> UploadAsync(byte[] content, CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not { } id || id == Guid.Empty) return new(PhotoStatus.Unauthenticated);
        var profile = await accounts.FindByIdAsync(id, cancellationToken);
        if (profile is null) return new(PhotoStatus.NotFound);
        // A foto do artista é pública; a de quem é somente cliente fica privada (decisão 0018).
        var isPublic = profile.IsArtist;
        var key = await photos.StoreAsync(profile.Id, content, isPublic, cancellationToken);
        var previous = profile.SetPhoto(key, isPublic);
        var urls = await photos.ResolveAsync(key, isPublic, cancellationToken);
        var publish = isPublic || previous?.Public == true;
        var change = new PhotoChange(photos.Objects(key), previous is null ? [] : Obsolete(previous),
            publish ? new ProfilePhotoChanged(profile.Id, profile.PhotoRevision, isPublic ? urls?.Url : null, isPublic ? urls?.ThumbnailUrl : null) : null);
        await store.SavePhotoAsync(profile, change, cancellationToken);
        return new(PhotoStatus.Updated, urls);
    }

    public async Task<PhotoResult> RemoveAsync(CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not { } id || id == Guid.Empty) return new(PhotoStatus.Unauthenticated);
        var profile = await accounts.FindByIdAsync(id, cancellationToken);
        if (profile is null) return new(PhotoStatus.NotFound);
        var previous = profile.RemovePhoto();
        if (previous is null) return new(PhotoStatus.Removed);
        var change = new PhotoChange([], Obsolete(previous), previous.Public ? new ProfilePhotoChanged(profile.Id, profile.PhotoRevision, null, null) : null);
        await store.SavePhotoAsync(profile, change, cancellationToken);
        return new(PhotoStatus.Removed);
    }

    private IReadOnlyList<StoredPhoto> Obsolete(StoredPhoto previous) =>
        photos.Objects(previous.Key).Select(item => new StoredPhoto(item, previous.Public)).ToArray();
}
