using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using InkLink.Identity.Application.Registration;
using InkLink.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace InkLink.Identity.Infrastructure.Registration;

/// <summary>Configuração do cadastro (decisão 0020). O segredo dos códigos vem do ambiente ou de User Secrets.</summary>
public sealed class RegistrationOptions
{
    public const string Section = "Registration";
    public string TermsVersion { get; set; } = "2026-11-provisorio";
    public string CodeSecret { get; set; } = string.Empty;
}

/// <summary>SMTP do e-mail (SES, Resend, SendGrid ou Mailpit local) e simulador de SMS dos ambientes locais.</summary>
public sealed class ContactOptions
{
    public const string Section = "Contact";
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public bool SmtpSsl { get; set; } = true;
    public string SmtpUsername { get; set; } = string.Empty;
    public string SmtpPassword { get; set; } = string.Empty;
    public string From { get; set; } = "InkLink <nao-responda@inklink.local>";
    /// <summary>"Simulator" entrega o SMS como e-mail ao Mailpit; só é aceito em Development e Testing.</summary>
    public string SmsProvider { get; set; } = string.Empty;
}

internal sealed class EfRegistrationStore(IdentityDbContext database) : IRegistrationStore
{
    public Task<Profile?> FindAsync(Guid profileId, CancellationToken cancellationToken) =>
        database.Profiles.SingleOrDefaultAsync(profile => profile.Id == profileId, cancellationToken);

    public Task<VerificationCode?> OpenCodeAsync(Guid profileId, ContactChannel channel, CancellationToken cancellationToken) =>
        database.VerificationCodes.Where(code => code.ProfileId == profileId && code.Channel == channel && code.ClosedAt == null)
            .OrderByDescending(code => code.CreatedAt).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<DateTimeOffset>> SentSinceAsync(Guid profileId, ContactChannel channel, DateTimeOffset since, CancellationToken cancellationToken) =>
        await database.VerificationCodes.AsNoTracking()
            .Where(code => code.ProfileId == profileId && code.Channel == channel && code.CreatedAt >= since)
            .Select(code => code.CreatedAt).ToListAsync(cancellationToken);

    public Task<bool> PhoneConfirmedByAnotherAsync(string phoneNumber, Guid profileId, CancellationToken cancellationToken) =>
        database.Profiles.AnyAsync(profile => profile.Id != profileId && profile.PhoneNumber == phoneNumber && profile.PhoneConfirmedAt != null, cancellationToken);

    public void Add(VerificationCode code) => database.VerificationCodes.Add(code);

    public async Task<bool> SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ix_profiles_confirmed_phone" })
        {
            // Outra conta confirmou o mesmo celular ao mesmo tempo; a confirmação desta não é gravada.
            database.ChangeTracker.Clear();
            return false;
        }
    }
}

internal sealed class EfArtistRequestReader(IdentityDbContext database) : IArtistRequestReader
{
    public async Task<IReadOnlyList<ArtistRequestItem>> PendingAsync(CancellationToken cancellationToken) =>
        await (from profile in database.Profiles.AsNoTracking()
               join account in database.ExternalAccounts.AsNoTracking() on profile.Id equals account.ProfileId
               where !profile.IsArtist && profile.ArtistRequestedAt != null
               orderby profile.ArtistRequestedAt
               select new ArtistRequestItem(profile.Id, profile.DisplayName, profile.ArtistRequestedAt!.Value, account.Issuer, account.Subject))
            .ToListAsync(cancellationToken);
}

/// <summary>HMAC-SHA256 sobre conta, canal, destino e código; o código em claro nunca é guardado.</summary>
internal sealed class HmacCodeHasher(byte[] secret) : ICodeHasher
{
    public string Hash(Guid profileId, ContactChannel channel, string target, string code)
    {
        var message = Encoding.UTF8.GetBytes(string.Join('\n', profileId.ToString("N"), channel.ToString(), target, code));
        return Convert.ToHexStringLower(HMACSHA256.HashData(secret, message));
    }
}

/// <summary>
/// Envio dos códigos. E-mail por SMTP; SMS pelo simulador local (entregue como e-mail ao Mailpit) enquanto não
/// houver provedor contratado. Sem configuração, o canal responde indisponível e nada é simulado.
/// </summary>
internal sealed class SmtpContactMessenger(ContactOptions options, bool localEnvironment, ILogger<SmtpContactMessenger> logger) : IContactMessenger
{
    public const string SmsSimulatorDomain = "sms.inklink.test";

    public async Task SendAsync(ContactChannel channel, string target, string code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.SmtpHost)) throw new ContactUnavailable("SMTP is not configured.");
        using var message = new MailMessage { From = ParseAddress(options.From), BodyEncoding = Encoding.UTF8, SubjectEncoding = Encoding.UTF8 };
        if (channel == ContactChannel.Email)
        {
            message.To.Add(target);
            message.Subject = "Código de confirmação do InkLink";
            message.Body = $"Seu código de confirmação do InkLink é {code}.\n\nEle vale por 10 minutos. Se você não criou uma conta, ignore esta mensagem.";
        }
        else
        {
            if (!localEnvironment || !string.Equals(options.SmsProvider, "Simulator", StringComparison.OrdinalIgnoreCase))
                throw new ContactUnavailable("No SMS provider is configured.");
            // Simulador: o SMS chega ao Mailpit endereçado ao número, para conferência manual e nos roteiros.
            message.To.Add($"{target.TrimStart('+')}@{SmsSimulatorDomain}");
            message.Subject = $"SMS para {target}";
            message.Body = $"InkLink: seu código de confirmação é {code}. Vale por 10 minutos.";
        }
        using var client = new SmtpClient(options.SmtpHost, options.SmtpPort) { EnableSsl = options.SmtpSsl, Timeout = 10_000 };
        if (!string.IsNullOrEmpty(options.SmtpUsername)) client.Credentials = new NetworkCredential(options.SmtpUsername, options.SmtpPassword);
        try
        {
            await client.SendMailAsync(message, cancellationToken);
        }
        catch (Exception exception) when (exception is SmtpException or InvalidOperationException or IOException)
        {
            // O destino e o código não entram no log.
            logger.LogWarning("Confirmation {Channel} could not be sent: {FailureType}", channel, exception.GetType().Name);
            throw new ContactUnavailable("The confirmation message could not be sent.");
        }
    }

    private static MailAddress ParseAddress(string value) => new(value);
}
