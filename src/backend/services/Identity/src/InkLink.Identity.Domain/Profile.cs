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

    // Cadastro pelo próprio usuário (decisão 0020): a conta nasce pendente e só fica ativa depois de aceitar
    // os termos e confirmar e-mail e celular. Contas administrativas e anteriores nascem ativas.

    /// <summary>Conta criada pela página de cadastro e ainda não confirmada.</summary>
    public bool RegistrationPending { get; private set; }
    public string? TermsVersion { get; private set; }
    public DateTimeOffset? TermsAcceptedAt { get; private set; }
    /// <summary>E-mail confirmado por código (o endereço vem do provedor de identidade).</summary>
    public string? ConfirmedEmail { get; private set; }
    public DateTimeOffset? EmailConfirmedAt { get; private set; }
    /// <summary>Celular em E.164.</summary>
    public string? PhoneNumber { get; private set; }
    public DateTimeOffset? PhoneConfirmedAt { get; private set; }
    public DateTimeOffset? ArtistRequestedAt { get; private set; }

    public static Profile CreatePending(string displayName)
    {
        var profile = Create(displayName);
        profile.RegistrationPending = true;
        return profile;
    }

    public ArtistRequest ArtistRequestStatus => IsArtist ? ArtistRequest.Approved : ArtistRequestedAt is null ? ArtistRequest.None : ArtistRequest.Pending;

    public void AcceptTerms(string version, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        TermsVersion = version;
        TermsAcceptedAt = now;
        CompleteRegistrationIfReady();
    }

    /// <summary>Troca o celular; um número novo exige nova confirmação.</summary>
    public void ChangePhone(string phoneNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);
        if (phoneNumber == PhoneNumber) return;
        PhoneNumber = phoneNumber;
        PhoneConfirmedAt = null;
    }

    public void ConfirmEmail(string address, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        ConfirmedEmail = address;
        EmailConfirmedAt = now;
        CompleteRegistrationIfReady();
    }

    public void ConfirmPhone(string phoneNumber, DateTimeOffset now)
    {
        if (phoneNumber != PhoneNumber) throw new InvalidOperationException("The confirmed number is not the current phone.");
        PhoneConfirmedAt = now;
        CompleteRegistrationIfReady();
    }

    /// <summary>Solicita a função Artist; a ativação depende de aprovação administrativa.</summary>
    public bool RequestArtist(DateTimeOffset now)
    {
        if (RegistrationPending) throw new InvalidOperationException("Confirm the registration before requesting the Artist role.");
        if (IsArtist || ArtistRequestedAt is not null) return false;
        ArtistRequestedAt = now;
        return true;
    }

    private void CompleteRegistrationIfReady()
    {
        if (RegistrationPending && TermsAcceptedAt is not null && EmailConfirmedAt is not null && PhoneConfirmedAt is not null)
            RegistrationPending = false;
    }
}

public enum ArtistRequest { None, Pending, Approved }

public sealed record StoredPhoto(string Key, bool Public);
