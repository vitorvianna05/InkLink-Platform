namespace InkLink.Identity.Domain;

public enum ContactChannel { Email, Phone }

public enum CodeCheck { Confirmed, Invalid, Expired, Exhausted }

/// <summary>
/// Código de confirmação de um canal (decisão 0020): uso único, válido por tempo limitado e para o destino
/// a que foi enviado, com tentativas limitadas. Guarda apenas o resumo (HMAC) do código.
/// </summary>
public sealed class VerificationCode
{
    public const int MaxAttempts = 5;
    public static readonly TimeSpan Validity = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan ResendInterval = TimeSpan.FromSeconds(60);
    public const int MaxSendsPerHour = 5;

    private VerificationCode() { }
    public Guid Id { get; private set; }
    public Guid ProfileId { get; private set; }
    public ContactChannel Channel { get; private set; }
    public string Target { get; private set; } = string.Empty;
    public string CodeHash { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public int Attempts { get; private set; }
    /// <summary>Uso concluído ou código substituído por um reenvio.</summary>
    public DateTimeOffset? ClosedAt { get; private set; }

    public static VerificationCode Issue(Guid profileId, ContactChannel channel, string target, string codeHash, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(codeHash);
        return new VerificationCode
        {
            Id = Guid.CreateVersion7(),
            ProfileId = profileId,
            Channel = channel,
            Target = target,
            CodeHash = codeHash,
            CreatedAt = now,
            ExpiresAt = now.Add(Validity),
        };
    }

    public void Close(DateTimeOffset now) => ClosedAt ??= now;

    /// <summary>Confere o resumo informado; tentativas erradas contam até o limite, que encerra o código.</summary>
    public CodeCheck Check(string candidateHash, DateTimeOffset now)
    {
        if (ClosedAt is not null) return CodeCheck.Expired;
        if (now >= ExpiresAt) { ClosedAt = now; return CodeCheck.Expired; }
        if (Attempts >= MaxAttempts) { ClosedAt = now; return CodeCheck.Exhausted; }
        if (System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.ASCII.GetBytes(candidateHash), System.Text.Encoding.ASCII.GetBytes(CodeHash)))
        {
            ClosedAt = now;
            return CodeCheck.Confirmed;
        }
        Attempts++;
        if (Attempts >= MaxAttempts) { ClosedAt = now; return CodeCheck.Exhausted; }
        return CodeCheck.Invalid;
    }
}
