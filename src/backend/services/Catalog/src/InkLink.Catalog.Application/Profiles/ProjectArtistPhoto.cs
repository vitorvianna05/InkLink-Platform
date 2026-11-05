namespace InkLink.Catalog.Application.Profiles;

/// <summary>Envelope de identity.profile-photo-changed.v1 (decisão 0018), contrato publicado pela Identity.</summary>
public sealed record ProfilePhotoEnvelope(Guid MessageId, string Type, int SchemaVersion, DateTimeOffset OccurredAt, string? CorrelationId,
    string? CausationId, ProfilePhotoData Data, string? TraceParent = null);

public sealed record ProfilePhotoData(Guid ProfileId, int Revision, string? PhotoUrl, string? PhotoThumbnailUrl);

/// <summary>Foto pública validada, pronta para a projeção.</summary>
public sealed record ArtistPhotoChange(Guid OwnerId, int Revision, string? PhotoUrl, string? ThumbnailUrl);

/// <summary>Evento inválido: vai para a fila de inválidos, sem nova tentativa.</summary>
public sealed class InvalidPhotoEvent(string message) : Exception(message);

/// <summary>Projeção da foto do artista; aplica somente revisões mais novas que a gravada.</summary>
public interface IArtistPhotoProjection
{
    Task ApplyAsync(ArtistPhotoChange change, CancellationToken cancellationToken);
}

/// <summary>
/// Atualiza a foto pública do artista a partir do evento da Identity. A revisão crescente descarta repetições
/// e eventos fora de ordem, o que torna o consumo idempotente sem tabela de Inbox (arquitetura §7.3).
/// </summary>
public sealed class ProjectArtistPhoto(IArtistPhotoProjection projection)
{
    public const string EventType = "identity.profile-photo-changed.v1";

    public Task ExecuteAsync(ProfilePhotoEnvelope envelope, CancellationToken cancellationToken)
    {
        if (envelope.Type != EventType || envelope.SchemaVersion != 1) throw new InvalidPhotoEvent("Unsupported event type or version.");
        if (envelope.MessageId == Guid.Empty || envelope.Data is null) throw new InvalidPhotoEvent("Envelope identity and data are required.");
        var data = envelope.Data;
        var url = PublicHttps(data.PhotoUrl);
        var thumbnail = PublicHttps(data.PhotoThumbnailUrl);
        if (data.ProfileId == Guid.Empty || data.Revision < 1 || (url is null) != (thumbnail is null)) throw new InvalidPhotoEvent("Invalid photo data.");
        return projection.ApplyAsync(new ArtistPhotoChange(data.ProfileId, data.Revision, url, thumbnail), cancellationToken);
    }

    private static string? PublicHttps(string? text)
    {
        if (text is null) return null;
        if (text.Length > 2048 || !Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidPhotoEvent("Photo URLs must be public HTTPS addresses.");
        return text;
    }
}
