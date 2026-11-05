namespace InkLink.Catalog.Infrastructure.Persistence;

/// <summary>
/// Objeto do armazenamento a remover (decisão 0018). Reservado antes do envio, com prazo; a gravação
/// da obra remove a reserva na mesma transação. Sobras de falhas são removidas pelo worker.
/// </summary>
public sealed class ObjectCleanup
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Key { get; set; } = string.Empty;
    public DateTimeOffset DueAt { get; set; }
    public int Attempts { get; set; }
}
