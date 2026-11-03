using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using InkLink.Engagements.Application.Accounts;
using InkLink.Engagements.Domain;

namespace InkLink.Engagements.Application.Negotiations;

public sealed class ManageEngagements(ICurrentEngagementsAccount current, IEngagementStore store, ICatalogDirectory catalog)
{
    public async Task<Creation<EngagementView>> RequestAsync(RequestInput input, Guid key, CancellationToken cancellationToken)
    {
        var account = AccountAccess.Require(current);
        EngagementRules.Id(key, "idempotencyKey");
        EngagementRules.Id(input.ArtistId, "artistId");
        if (input.FlashId.HasValue) EngagementRules.Id(input.FlashId.Value, "flashId");
        input = input with { Idea = EngagementRules.Text(input.Idea, "idea", 4000), BodyRegion = EngagementRules.Text(input.BodyRegion, "bodyRegion", 120) };
        EngagementRules.Number(input.ApproximateSizeCm, "approximateSizeCm", 300);
        var fingerprint = Fingerprint(new { input.ArtistId, input.Idea, input.BodyRegion, Size = input.ApproximateSizeCm.ToString("G29", CultureInfo.InvariantCulture), input.FlashId });
        var previous = await store.FindRequestAsync(account.Id, key, cancellationToken);
        if (previous is not null)
        {
            EnsureSame(previous.RequestFingerprint, fingerprint);
            return new((await store.ReadAsync(previous.Id, account.Id, cancellationToken))!, false);
        }
        var context = await catalog.GetAsync(input.ArtistId, input.FlashId, cancellationToken);
        var snapshot = new CatalogSnapshot(context.ArtistId, context.Name, context.Studio, context.City, context.StartingPrice, context.Flash);
        var engagement = Engagement.Create(account.Id, context.ArtistAccountId, input.ArtistId, input.FlashId, input.Idea, input.BodyRegion,
            input.ApproximateSizeCm, JsonSerializer.Serialize(snapshot), key, fingerprint, DateTimeOffset.UtcNow);
        var result = await store.CreateAsync(engagement, PrepareEngagementCreation.Apply, cancellationToken);
        EnsureSame(result.Value.RequestFingerprint, fingerprint);
        return new((await store.ReadAsync(result.Value.Id, account.Id, cancellationToken))!, result.Created);
    }

    public async Task<EngagementView> ReadAsync(Guid id, CancellationToken cancellationToken) =>
        await store.ReadAsync(id, AccountAccess.Require(current).Id, cancellationToken) ?? throw NotFound();

    public async Task<Page<EngagementView>> ListAsync(string perspective, int limit, string? cursor, CancellationToken cancellationToken)
    {
        var account = AccountAccess.Require(current, perspective == "artist");
        if (perspective is not ("client" or "artist")) throw EngagementsFailure.Invalid("perspective", "Use client or artist.");
        if (limit is < 1 or > 100) throw EngagementsFailure.Invalid("limit", "Use 1 to 100.");
        Guid? after = null;
        if (cursor is not null)
        {
            try
            {
                if (cursor.Length > 500) throw new FormatException();
                var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|');
                if (parts.Length != 4 || parts[0] != "v1" || parts[1] != account.Id.ToString() || parts[2] != perspective || !Guid.TryParse(parts[3], out var position) || position == Guid.Empty)
                    throw new FormatException();
                after = position;
            }
            catch (FormatException) { throw EngagementsFailure.Invalid("cursor", "Cursor does not match this query."); }
        }
        var page = await store.ListAsync(account.Id, perspective, limit, after, cancellationToken);
        return page with { NextCursor = page.HasMore ? Convert.ToBase64String(Encoding.UTF8.GetBytes($"v1|{account.Id}|{perspective}|{page.Items[^1].Id}")) : null };
    }

    public Task<Creation<ProposalView>> PresentAsync(Guid id, ProposalInput input, Guid key, CancellationToken cancellationToken)
    {
        var account = AccountAccess.Require(current, artist: true);
        EngagementRules.Id(key, "idempotencyKey");
        input = input with { Description = EngagementRules.Text(input.Description, "description", 4000) };
        EngagementRules.Number(input.Amount, "amount", 9999999.99m);
        var fingerprint = Fingerprint(new { input.Description, Amount = input.Amount.ToString("G29", CultureInfo.InvariantCulture) });
        return store.ChangeAsync(id, account.Id, true, key, null, (engagement, proposals) =>
        {
            var repeated = proposals.SingleOrDefault(p => p.RequestKey == key);
            if (repeated is not null)
            {
                EnsureSame(repeated.RequestFingerprint, fingerprint);
                return new EngagementChange<Creation<ProposalView>>(new(View(repeated), false));
            }
            var previous = proposals.SingleOrDefault(p => p.Id == engagement.CurrentProposalId);
            var proposal = Proposal.Create(id, key, fingerprint, input.Description, input.Amount, DateTimeOffset.UtcNow);
            try { engagement.Present(proposal, previous); }
            catch (InvalidOperationException exception) { throw new EngagementsFailure(EngagementsError.Conflict, exception.Message); }
            return new EngagementChange<Creation<ProposalView>>(new(View(proposal), true), proposal);
        }, cancellationToken);
    }

    public Task<ProposalView> AcceptAsync(Guid id, Guid proposalId, CancellationToken cancellationToken) =>
        store.ChangeAsync(id, AccountAccess.Require(current).Id, false, null, proposalId, (engagement, proposals) =>
        {
            // The locked snapshot includes the current proposal. Distinguish an old version from a foreign ID without exposing another engagement.
            var proposal = proposals.SingleOrDefault(p => p.Id == proposalId) ?? throw NotFound();
            try { engagement.Accept(proposal, DateTimeOffset.UtcNow); }
            catch (InvalidOperationException exception) { throw new EngagementsFailure(EngagementsError.Conflict, exception.Message); }
            return new EngagementChange<ProposalView>(View(proposal));
        }, cancellationToken);

    public async Task<ProposalView> ReadProposalAsync(Guid id, Guid proposalId, CancellationToken cancellationToken) =>
        await store.ReadProposalAsync(id, proposalId, AccountAccess.Require(current).Id, cancellationToken) ?? throw NotFound();

    public async Task<Page<ProposalView>> ListProposalsAsync(Guid id, int limit, string? cursor, CancellationToken cancellationToken)
    {
        var account = AccountAccess.Require(current);
        if (limit is < 1 or > 100) throw EngagementsFailure.Invalid("limit", "Use 1 to 100.");
        Guid? after = null;
        var prefix = $"v2|proposals|{account.Id}|{id}|";
        if (cursor is not null)
        {
            try
            {
                if (cursor.Length > 500) throw new FormatException();
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
                if (!decoded.StartsWith(prefix, StringComparison.Ordinal) || !Guid.TryParse(decoded[prefix.Length..], out var position) || position == Guid.Empty)
                    throw new FormatException();
                after = position;
            }
            catch (FormatException) { throw EngagementsFailure.Invalid("cursor", "Cursor does not match this query."); }
        }
        var page = await store.ListProposalsAsync(id, account.Id, limit, after, cancellationToken);
        return page with { NextCursor = page.HasMore ? Convert.ToBase64String(Encoding.UTF8.GetBytes(prefix + page.Items[^1].Id)) : null };
    }

    private static ProposalView View(Proposal proposal) => new(proposal.Id, proposal.Description, proposal.Amount, "BRL", proposal.State.ToString(), proposal.CreatedAt, proposal.AcceptedAt);

    private static string Fingerprint<T>(T input) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(input))));
    public static void EnsureSame(string stored, string fingerprint)
    {
        if (stored != fingerprint) throw new EngagementsFailure(EngagementsError.Conflict, "engagements.idempotency_conflict");
    }
    public static EngagementsFailure NotFound() => new(EngagementsError.NotFound, "engagements.not_found");
}
