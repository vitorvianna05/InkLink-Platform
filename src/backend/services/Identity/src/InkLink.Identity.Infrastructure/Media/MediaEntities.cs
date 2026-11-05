namespace InkLink.Identity.Infrastructure.Media;

/// <summary>Objeto do armazenamento a remover (decisão 0018); reservado antes do envio e confirmado pela gravação.</summary>
public sealed class ObjectCleanup
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Key { get; set; } = string.Empty;
    public bool Private { get; set; }
    public DateTimeOffset DueAt { get; set; }
    public int Attempts { get; set; }
}

/// <summary>Evento gravado na mesma transação da foto e publicado depois (Outbox, padrão de P05).</summary>
public sealed class OutboxEvent
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public DateTimeOffset? QuarantinedAt { get; set; }

    public void RecordFailure(string error, DateTimeOffset now)
    {
        Attempts++;
        LastError = error.Length <= 200 ? error : error[..200];
        if (Attempts >= 10) { QuarantinedAt = now; NextAttemptAt = null; return; }
        NextAttemptAt = now.AddSeconds(Math.Min(300, 2 * Math.Pow(2, Attempts - 1)));
    }
}
