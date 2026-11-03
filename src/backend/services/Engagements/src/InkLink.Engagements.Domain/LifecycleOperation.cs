namespace InkLink.Engagements.Domain;

public sealed class LifecycleOperation
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid EngagementId { get; private set; }
    public string? TraceParent { get; private set; }
    public string? CorrelationId { get; private set; }
    public string Kind { get; private set; } = "";
    public string Status { get; private set; } = "Pending";
    public EngagementState PreviousState { get; private set; }
    public int Attempts { get; private set; }
    public string? Error { get; private set; }
    public DateTimeOffset? NextAttemptAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public static LifecycleOperation Create(Engagement e, string kind, string? traceParent = null, string? correlationId = null) =>
        new() { EngagementId = e.Id, Kind = kind, PreviousState = e.State, TraceParent = traceParent, CorrelationId = correlationId };
    public void Defer(string reason)
    {
        Error = reason; UpdatedAt = DateTimeOffset.UtcNow; NextAttemptAt = UpdatedAt.AddSeconds(5);
    }
    public void Complete() { Status = "Completed"; Error = null; UpdatedAt = DateTimeOffset.UtcNow; }
    public void Reject(string error) { Status = "Rejected"; Error = error; UpdatedAt = DateTimeOffset.UtcNow; }
    public void Fail(string error)
    {
        Attempts++; Error = error; UpdatedAt = DateTimeOffset.UtcNow;
        Status = Attempts >= 10 ? "Quarantined" : "Pending";
        NextAttemptAt = UpdatedAt.AddSeconds(Math.Min(300, 2 * Math.Pow(2, Attempts)));
    }
    public void Retry(Engagement e)
    {
        if (Status == "Rejected") PreviousState = e.State;
        Status = "Pending"; Attempts = 0; Error = null; NextAttemptAt = null;
    }
}
