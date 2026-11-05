namespace InkLink.Identity.Domain;

public sealed class Profile
{
    private Profile() { }
    public Guid Id { get; private set; }
    public bool IsArtist { get; private set; }
    public void ActivateArtist() => IsArtist = true;
    public void Rename(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        DisplayName = displayName.Trim();
    }
    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>Prefixo da foto de perfil no armazenamento (decisão 0018); uma por conta.</summary>
    public string? PhotoKey { get; private set; }
    /// <summary>Foto pública (conta com função Artist no envio) ou privada (somente cliente).</summary>
    public bool PhotoPublic { get; private set; }
    /// <summary>Revisão crescente da foto, usada pelas projeções para descartar eventos antigos.</summary>
    public int PhotoRevision { get; private set; }

    /// <summary>Troca a foto e devolve a anterior, quando havia.</summary>
    public StoredPhoto? SetPhoto(string key, bool isPublic)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var previous = PhotoKey is null ? null : new StoredPhoto(PhotoKey, PhotoPublic);
        PhotoKey = key;
        PhotoPublic = isPublic;
        PhotoRevision++;
        return previous;
    }

    public StoredPhoto? RemovePhoto()
    {
        if (PhotoKey is null) return null;
        var previous = new StoredPhoto(PhotoKey, PhotoPublic);
        PhotoKey = null;
        PhotoPublic = false;
        PhotoRevision++;
        return previous;
    }

    public static Profile Create(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        return new Profile { Id = Guid.CreateVersion7(), DisplayName = displayName.Trim() };
    }
}

public sealed record StoredPhoto(string Key, bool Public);
