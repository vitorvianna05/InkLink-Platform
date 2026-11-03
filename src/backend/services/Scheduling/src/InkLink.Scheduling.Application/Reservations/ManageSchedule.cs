using System.Text;
using InkLink.Scheduling.Application.Accounts;
using InkLink.Scheduling.Domain;

namespace InkLink.Scheduling.Application.Reservations;

public sealed class ManageSchedule(ICurrentSchedulingAccount current, ISchedulingStore store, IEngagementDirectory engagements,
    IParticipantDirectory profiles, TimeProvider clock)
{
    public async Task<AvailabilityView> DefineAsync(DateOnly date, AvailabilityInput input, CancellationToken ct)
    {
        var account = AccountAccess.Require(current, artist: true);
        ArtistCalendar.Zone(input.TimeZoneId);
        await store.ReplaceAvailabilityAsync(account.Id, date, input.TimeZoneId, snapshot =>
        {
            snapshot.Calendar.SelectZone(input.TimeZoneId, snapshot.HasEntries);
            try { return snapshot.Calendar.Define(date, input.Windows, snapshot.Reservations); }
            catch (ReservedAvailabilityConflict) { throw new SchedulingFailure(SchedulingError.Conflict, "scheduling.reserved_availability"); }
        }, ct);
        return await store.AvailabilityAsync(account.Id, date, ArtistCalendar.DefaultTimeZone, ct);
    }
    public Task<AvailabilityView> OwnAvailabilityAsync(DateOnly date, CancellationToken ct) =>
        store.AvailabilityAsync(AccountAccess.Require(current, artist: true).Id, date, ArtistCalendar.DefaultTimeZone, ct);
    public async Task<AvailabilityView> AvailabilityAsync(Guid engagement, DateOnly date, CancellationToken ct)
    {
        var account = AccountAccess.Require(current); var context = await engagements.ReadAsync(engagement, ct);
        RequireParticipant(context, account.Id);
        return await store.AvailabilityAsync(context.ArtistAccountId, date, ArtistCalendar.DefaultTimeZone, ct);
    }
    public async Task<Creation<ReservationView>> ConfirmAsync(Guid operation, ReserveInput input, CancellationToken ct)
    {
        var account = AccountAccess.Require(current, artist: true);
        if (input.EngagementId == Guid.Empty) throw SchedulingFailure.Invalid("engagementId", "Nonempty UUID required.");
        if (operation == Guid.Empty) throw SchedulingFailure.Invalid("operationId", "Nonempty UUID required.");
        ReservationView Repeat(ReservationView previous)
        {
            if (previous.EngagementId != input.EngagementId || previous.Start != input.Start || previous.End != input.End)
                throw new SchedulingFailure(SchedulingError.Conflict, "scheduling.idempotency_conflict");
            return previous;
        }
        var previous = await store.FindOperationAsync(account.Id, operation, ct);
        if (previous is not null) return new(Repeat(previous), false);
        var context = await engagements.ReadAsync(input.EngagementId, ct);
        if (context.ArtistAccountId != account.Id) throw NotFound();
        if (context.State != "Agreed" || context.ProposalState != "Accepted" || context.ProposalId is null || context.Amount is not > 0)
            throw new SchedulingFailure(SchedulingError.Conflict, "scheduling.proposal_not_accepted");
        var clientName = await profiles.NameAsync(context.ClientId, ct);
        return await store.ConfirmAsync(account.Id, operation, DateOnly.FromDateTime(input.Start.DateTime), ArtistCalendar.DefaultTimeZone, Repeat, snapshot =>
            Reservation.Confirm(operation, context.Id, context.ClientId, snapshot.Calendar, context.ProposalId.Value,
                input.Start, input.End, clientName, context.ArtistName, context.Project, context.Amount.Value, snapshot.Windows, clock.GetUtcNow()), ct);
    }
    public async Task<ReservationView> ReadAsync(Guid id, CancellationToken ct) =>
        await store.ReadAsync(id, AccountAccess.Require(current).Id, ct) ?? throw NotFound();
    public async Task<ReservationView> OperationAsync(Guid id, CancellationToken ct) =>
        await store.FindOperationAsync(AccountAccess.Require(current, artist: true).Id, id, ct) ?? throw NotFound();
    public async Task<Page<ReservationView>> ListAsync(string perspective, DateOnly from, DateOnly to, int limit, string? cursor, CancellationToken ct)
    {
        var account = AccountAccess.Require(current, artist: perspective == "artist");
        if (perspective is not ("client" or "artist") || to <= from || to.DayNumber - from.DayNumber > 93 || limit is < 1 or > 100)
            throw SchedulingFailure.Invalid("query", "Use client/artist, a [from,to) range up to 93 days and limit 1..100.");
        var prefix = $"v1|{account.Id}|{perspective}|{from:yyyy-MM-dd}|{to:yyyy-MM-dd}|";
        Guid? after = null;
        if (cursor is not null)
        {
            try
            {
                if (cursor.Length > 500) throw new FormatException();
                var value = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
                if (!value.StartsWith(prefix, StringComparison.Ordinal) || !Guid.TryParse(value[prefix.Length..], out var id)) throw new FormatException();
                after = id;
            }
            catch (FormatException) { throw SchedulingFailure.Invalid("cursor", "Cursor does not match the query."); }
        }
        var page = await store.ListAsync(account.Id, perspective, from, to, limit, after, ct);
        return page with { NextCursor = page.HasMore ? Convert.ToBase64String(Encoding.UTF8.GetBytes(prefix + page.Items[^1].Id)) : null };
    }
    private static void RequireParticipant(SchedulingContext context, Guid account)
    {
        if (context.ClientId != account && context.ArtistAccountId != account) throw NotFound();
    }
    public static SchedulingFailure NotFound() => new(SchedulingError.NotFound, "scheduling.not_found");
}
