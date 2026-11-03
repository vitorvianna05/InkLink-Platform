namespace InkLink.Engagements.Domain;

public enum EngagementState { Negotiating, Agreed, Closing, Closed }
public enum ProposalState { Pending, Superseded, Accepted }

public sealed class InvalidEngagementData(string field, string message) : Exception(message)
{
    public string Field { get; } = field;
}

public static class EngagementRules
{
    // PostgreSQL persists microseconds; retries must return the same instant as the original response.
    public static DateTimeOffset Instant(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - utc.Ticks % 10, TimeSpan.Zero);
    }

    public static string Text(string? value, string field, int maximum)
    {
        var text = value?.Trim() ?? string.Empty;
        if (text.Length == 0 || (value?.Length ?? 0) > maximum) throw new InvalidEngagementData(field, $"Required text, maximum {maximum} characters.");
        return text;
    }

    public static decimal Number(decimal value, string field, decimal maximum)
    {
        if (value <= 0 || value > maximum || decimal.Round(value, 2) != value)
            throw new InvalidEngagementData(field, $"Must be positive, at most {maximum}, with up to two decimal places.");
        return value;
    }

    public static Guid Id(Guid value, string field) => value != Guid.Empty ? value : throw new InvalidEngagementData(field, "A nonempty UUID is required.");
}

public sealed class Engagement
{
    private Engagement() { }
    public Guid Id { get; private set; }
    public Guid ClientId { get; private set; }
    public Guid ArtistAccountId { get; private set; }
    public Guid ArtistId { get; private set; }
    public Guid? FlashId { get; private set; }
    public string Idea { get; private set; } = string.Empty;
    public string BodyRegion { get; private set; } = string.Empty;
    public decimal ApproximateSizeCm { get; private set; }
    public string CatalogSnapshotJson { get; private set; } = string.Empty;
    public Guid RequestKey { get; private set; }
    public string RequestFingerprint { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public EngagementState State { get; private set; }
    public Guid? CurrentProposalId { get; private set; }
    public long Revision { get; private set; }
    public bool RequiresReservation { get; private set; }
    public bool ActivationRequested { get; private set; }
    public void ReservationConfirmed()
    {
        if (RequiresReservation && State == EngagementState.Agreed) ActivationRequested = true;
    }
    public void RequireReservation() => RequiresReservation = true;
    public void BeginClosing()
    {
        if (State is EngagementState.Closing or EngagementState.Closed) return;
        State = EngagementState.Closing; Revision++;
    }
    public void RejectClosing(EngagementState previous)
    {
        if (State != EngagementState.Closing || previous is not (EngagementState.Negotiating or EngagementState.Agreed))
            throw new InvalidOperationException("engagements.invalid_closure");
        State = previous; Revision++;
    }
    public void CompleteClosing()
    {
        if (State == EngagementState.Closed) return;
        if (State != EngagementState.Closing) throw new InvalidOperationException("engagements.invalid_closure");
        State = EngagementState.Closed; Revision++;
    }


    public static Engagement Create(Guid client, Guid artistAccount, Guid artist, Guid? flash, string idea, string bodyRegion,
        decimal size, string snapshot, Guid requestKey, string fingerprint, DateTimeOffset now)
    {
        EngagementRules.Id(client, "clientId");
        EngagementRules.Id(artistAccount, "artistAccountId");
        EngagementRules.Id(artist, "artistId");
        EngagementRules.Id(requestKey, "idempotencyKey");
        if (flash.HasValue) EngagementRules.Id(flash.Value, "flashId");
        if (client == artistAccount) throw new InvalidEngagementData("artistId", "Self service is not allowed.");
        return new Engagement
        {
            Id = Guid.CreateVersion7(),
            ClientId = client,
            ArtistAccountId = artistAccount,
            ArtistId = artist,
            FlashId = flash,
            Idea = EngagementRules.Text(idea, "idea", 4000),
            BodyRegion = EngagementRules.Text(bodyRegion, "bodyRegion", 120),
            ApproximateSizeCm = EngagementRules.Number(size, "approximateSizeCm", 300),
            CatalogSnapshotJson = snapshot,
            RequestKey = requestKey,
            RequestFingerprint = fingerprint,
            CreatedAt = EngagementRules.Instant(now),
            Revision = 1
        };
    }

    public void Present(Proposal proposal, Proposal? previous)
    {
        if (State != EngagementState.Negotiating) throw new InvalidOperationException("engagements.already_agreed");
        if (proposal.EngagementId != Id || proposal.State != ProposalState.Pending || proposal.Id == CurrentProposalId
            || previous?.Id != CurrentProposalId || (previous is not null && previous.EngagementId != Id))
            throw new InvalidOperationException("engagements.proposal_mismatch");
        previous?.Supersede();
        CurrentProposalId = proposal.Id;
        Revision++;
    }

    public void Accept(Proposal proposal, DateTimeOffset now)
    {
        if (proposal.EngagementId != Id || CurrentProposalId != proposal.Id) throw new InvalidOperationException("engagements.proposal_not_current");
        if (State == EngagementState.Agreed && proposal.State == ProposalState.Accepted) return;
        if (State != EngagementState.Negotiating || proposal.State != ProposalState.Pending) throw new InvalidOperationException("engagements.proposal_not_pending");
        proposal.Accept(now);
        State = EngagementState.Agreed;
        Revision++;
    }
}

public sealed class Proposal
{
    private Proposal() { }
    public Guid Id { get; private set; }
    public Guid EngagementId { get; private set; }
    public Guid RequestKey { get; private set; }
    public string RequestFingerprint { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? AcceptedAt { get; private set; }
    public ProposalState State { get; private set; }

    public static Proposal Create(Guid engagement, Guid key, string fingerprint, string description, decimal amount, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        EngagementId = EngagementRules.Id(engagement, "engagementId"),
        RequestKey = EngagementRules.Id(key, "idempotencyKey"),
        RequestFingerprint = fingerprint,
        Description = EngagementRules.Text(description, "description", 4000),
        Amount = EngagementRules.Number(amount, "amount", 9999999.99m),
        CreatedAt = EngagementRules.Instant(now)
    };

    internal void Supersede()
    {
        if (State != ProposalState.Pending) throw new InvalidOperationException("engagements.proposal_not_pending");
        State = ProposalState.Superseded;
    }

    internal void Accept(DateTimeOffset now)
    {
        State = ProposalState.Accepted;
        AcceptedAt = EngagementRules.Instant(now);
    }
}
