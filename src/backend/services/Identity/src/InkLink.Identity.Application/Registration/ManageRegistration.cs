using System.Security.Cryptography;
using System.Text.RegularExpressions;
using InkLink.Identity.Domain;

namespace InkLink.Identity.Application.Registration;

/// <summary>Contato informado pelo provedor de identidade no token (escopo inklink.contact, decisão 0020).</summary>
public interface ICurrentContact
{
    string? Email { get; }
}

public interface IRegistrationStore
{
    Task<Profile?> FindAsync(Guid profileId, CancellationToken cancellationToken);
    Task<VerificationCode?> OpenCodeAsync(Guid profileId, ContactChannel channel, CancellationToken cancellationToken);
    Task<IReadOnlyList<DateTimeOffset>> SentSinceAsync(Guid profileId, ContactChannel channel, DateTimeOffset since, CancellationToken cancellationToken);
    Task<bool> PhoneConfirmedByAnotherAsync(string phoneNumber, Guid profileId, CancellationToken cancellationToken);
    void Add(VerificationCode code);
    /// <summary>Grava; devolve falso quando outra conta confirmou o mesmo celular ao mesmo tempo.</summary>
    Task<bool> SaveAsync(CancellationToken cancellationToken);
}

/// <summary>Resumo do código com segredo do ambiente; o código em claro nunca é guardado.</summary>
public interface ICodeHasher
{
    string Hash(Guid profileId, ContactChannel channel, string target, string code);
}

/// <summary>Envio do código por e-mail ou SMS; lança <see cref="ContactUnavailable"/> quando o canal não está disponível.</summary>
public interface IContactMessenger
{
    Task SendAsync(ContactChannel channel, string target, string code, CancellationToken cancellationToken);
}

public sealed class ContactUnavailable(string message) : Exception(message);

public sealed record RegistrationSettings(string TermsVersion);

public enum RegistrationError { Unauthenticated, Invalid, Conflict, TooManyRequests, Forbidden, Unavailable }

public sealed class RegistrationFailure(RegistrationError error, string code, string? field = null, DateTimeOffset? retryAt = null) : Exception(code)
{
    public RegistrationError Error { get; } = error;
    public string Code { get; } = code;
    public string? Field { get; } = field;
    public DateTimeOffset? RetryAt { get; } = retryAt;
}

public sealed record RegistrationView(
    string State, string? Email, bool EmailConfirmed, string? PhoneNumber, bool PhoneConfirmed,
    string TermsVersion, bool TermsAccepted, string ArtistRequest, DateTimeOffset? EmailResendAt, DateTimeOffset? PhoneResendAt);

/// <summary>Cadastro pelo próprio usuário: termos, celular, códigos de 8 dígitos e solicitação de Artist (decisão 0020).</summary>
public sealed partial class ManageRegistration(
    ICurrentUser current, ICurrentContact contact, IRegistrationStore store, ICodeHasher hasher, IContactMessenger messenger,
    RegistrationSettings settings, TimeProvider time)
{
    public async Task<RegistrationView> ReadAsync(CancellationToken cancellationToken) => await ViewAsync(await ProfileAsync(cancellationToken), cancellationToken);

    public async Task<RegistrationView> AcceptTermsAsync(string? version, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(version)) throw new RegistrationFailure(RegistrationError.Invalid, "registration.terms_version_required", "version");
        if (version != settings.TermsVersion) throw new RegistrationFailure(RegistrationError.Conflict, "registration.terms_outdated", "version");
        var profile = await ProfileAsync(cancellationToken);
        profile.AcceptTerms(version, time.GetUtcNow());
        await SaveAsync(cancellationToken);
        return await ViewAsync(profile, cancellationToken);
    }

    public async Task<RegistrationView> ChangePhoneAsync(string? phoneNumber, CancellationToken cancellationToken)
    {
        var normalized = NormalizePhone(phoneNumber) ?? throw new RegistrationFailure(RegistrationError.Invalid, "registration.phone_invalid", "phoneNumber");
        var profile = await ProfileAsync(cancellationToken);
        if (normalized != profile.PhoneNumber)
        {
            profile.ChangePhone(normalized);
            // O código pendente pertence ao número anterior.
            (await store.OpenCodeAsync(profile.Id, ContactChannel.Phone, cancellationToken))?.Close(time.GetUtcNow());
            await SaveAsync(cancellationToken);
        }
        return await ViewAsync(profile, cancellationToken);
    }

    public async Task<RegistrationView> SendCodeAsync(ContactChannel channel, CancellationToken cancellationToken)
    {
        var profile = await ProfileAsync(cancellationToken);
        var target = Target(profile, channel);
        if (IsConfirmed(profile, channel)) throw new RegistrationFailure(RegistrationError.Conflict, "registration.already_confirmed", "channel");
        var now = time.GetUtcNow();
        var sent = await store.SentSinceAsync(profile.Id, channel, now.AddHours(-1), cancellationToken);
        if (sent.Count > 0 && now - sent.Max() < VerificationCode.ResendInterval)
            throw new RegistrationFailure(RegistrationError.TooManyRequests, "registration.resend_too_soon", "channel", sent.Max().Add(VerificationCode.ResendInterval));
        if (sent.Count >= VerificationCode.MaxSendsPerHour)
            throw new RegistrationFailure(RegistrationError.TooManyRequests, "registration.send_limit", "channel", sent.Min().AddHours(1));
        (await store.OpenCodeAsync(profile.Id, channel, cancellationToken))?.Close(now);
        var code = RandomNumberGenerator.GetInt32(0, 100_000_000).ToString("D8", System.Globalization.CultureInfo.InvariantCulture);
        var issued = VerificationCode.Issue(profile.Id, channel, target, hasher.Hash(profile.Id, channel, target, code), now);
        store.Add(issued);
        await SaveAsync(cancellationToken);
        try
        {
            await messenger.SendAsync(channel, target, code, cancellationToken);
        }
        catch (ContactUnavailable)
        {
            // O envio falhou: o código não pode valer, mas conta no limite de envios.
            issued.Close(time.GetUtcNow());
            await SaveAsync(CancellationToken.None);
            throw new RegistrationFailure(RegistrationError.Unavailable, "registration.channel_unavailable", "channel");
        }
        return await ViewAsync(profile, cancellationToken);
    }

    public async Task<RegistrationView> VerifyCodeAsync(ContactChannel channel, string? code, CancellationToken cancellationToken)
    {
        if (code is null || !CodePattern().IsMatch(code)) throw new RegistrationFailure(RegistrationError.Invalid, "registration.code_invalid", "code");
        var profile = await ProfileAsync(cancellationToken);
        var target = Target(profile, channel);
        var open = await store.OpenCodeAsync(profile.Id, channel, cancellationToken);
        if (open is null || open.Target != target) throw new RegistrationFailure(RegistrationError.Invalid, "registration.code_invalid", "code");
        var now = time.GetUtcNow();
        var result = open.Check(hasher.Hash(profile.Id, channel, target, code), now);
        if (result == CodeCheck.Confirmed && channel == ContactChannel.Phone && await store.PhoneConfirmedByAnotherAsync(target, profile.Id, cancellationToken))
            result = CodeCheck.Invalid; // mesma resposta de código inválido: não revela a outra conta.
        if (result == CodeCheck.Confirmed)
        {
            if (channel == ContactChannel.Email) profile.ConfirmEmail(target, now);
            else profile.ConfirmPhone(target, now);
        }
        if (!await store.SaveAsync(cancellationToken)) result = CodeCheck.Invalid;
        return result switch
        {
            CodeCheck.Confirmed => await ViewAsync(profile, cancellationToken),
            CodeCheck.Expired => throw new RegistrationFailure(RegistrationError.Invalid, "registration.code_expired", "code"),
            CodeCheck.Exhausted => throw new RegistrationFailure(RegistrationError.TooManyRequests, "registration.code_attempts_exceeded", "code"),
            _ => throw new RegistrationFailure(RegistrationError.Invalid, "registration.code_invalid", "code"),
        };
    }

    public async Task<RegistrationView> RequestArtistAsync(CancellationToken cancellationToken)
    {
        var profile = await ProfileAsync(cancellationToken);
        if (profile.RegistrationPending) throw new RegistrationFailure(RegistrationError.Forbidden, "account.confirmation_required");
        if (profile.RequestArtist(time.GetUtcNow())) await SaveAsync(cancellationToken);
        return await ViewAsync(profile, cancellationToken);
    }

    /// <summary>E.164: aceita +DDI e números brasileiros de 10 ou 11 dígitos (DDD e número), aos quais acrescenta +55.</summary>
    public static string? NormalizePhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        var digits = new string(trimmed.Where(char.IsAsciiDigit).ToArray());
        if (trimmed.Any(c => !char.IsAsciiDigit(c) && c is not ('+' or ' ' or '-' or '(' or ')' or '.'))) return null;
        if (trimmed.StartsWith('+')) return digits.Length is >= 8 and <= 15 && digits[0] != '0' ? "+" + digits : null;
        return digits.Length is 10 or 11 && digits[0] != '0' ? "+55" + digits : null;
    }

    private async Task<Profile> ProfileAsync(CancellationToken cancellationToken)
    {
        if (current.UserId is not { } id || id == Guid.Empty) throw new RegistrationFailure(RegistrationError.Unauthenticated, "authentication.required");
        return await store.FindAsync(id, cancellationToken) ?? throw new RegistrationFailure(RegistrationError.Unauthenticated, "authentication.required");
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (!await store.SaveAsync(cancellationToken)) throw new RegistrationFailure(RegistrationError.Conflict, "registration.conflict");
    }

    private string Target(Profile profile, ContactChannel channel) => channel == ContactChannel.Email
        ? (string.IsNullOrWhiteSpace(contact.Email) ? throw new RegistrationFailure(RegistrationError.Unavailable, "registration.email_unavailable", "channel") : contact.Email.Trim().ToLowerInvariant())
        : profile.PhoneNumber ?? throw new RegistrationFailure(RegistrationError.Invalid, "registration.phone_required", "phoneNumber");

    private bool IsConfirmed(Profile profile, ContactChannel channel) => channel == ContactChannel.Email
        ? profile.EmailConfirmedAt is not null && profile.ConfirmedEmail == contact.Email?.Trim().ToLowerInvariant()
        : profile.PhoneConfirmedAt is not null;

    private async Task<RegistrationView> ViewAsync(Profile profile, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        async Task<DateTimeOffset?> ResendAt(ContactChannel channel)
        {
            var sent = await store.SentSinceAsync(profile.Id, channel, now.AddHours(-1), cancellationToken);
            if (sent.Count == 0) return null;
            var next = sent.Max().Add(VerificationCode.ResendInterval);
            if (sent.Count >= VerificationCode.MaxSendsPerHour) next = new[] { next, sent.Min().AddHours(1) }.Max();
            return next > now ? next : null;
        }
        return new(
            profile.RegistrationPending ? "Pending" : "Active",
            contact.Email?.Trim().ToLowerInvariant(), IsConfirmed(profile, ContactChannel.Email),
            profile.PhoneNumber, profile.PhoneConfirmedAt is not null,
            settings.TermsVersion, profile.TermsVersion == settings.TermsVersion,
            profile.ArtistRequestStatus.ToString(),
            await ResendAt(ContactChannel.Email), await ResendAt(ContactChannel.Phone));
    }

    [GeneratedRegex("^[0-9]{8}$")]
    private static partial Regex CodePattern();
}

/// <summary>Solicitações de Artist pendentes, para a aprovação administrativa.</summary>
public sealed record ArtistRequestItem(Guid ProfileId, string DisplayName, DateTimeOffset RequestedAt, string Issuer, string Subject);

public interface IArtistRequestReader
{
    Task<IReadOnlyList<ArtistRequestItem>> PendingAsync(CancellationToken cancellationToken);
}
