using InkLink.Scheduling.Domain;

namespace InkLink.Scheduling.Application.Reservations;

public sealed record AvailabilityInput(IReadOnlyList<Window> Windows, string TimeZoneId = ArtistCalendar.DefaultTimeZone);
public sealed record ReserveInput(Guid EngagementId, DateTimeOffset Start, DateTimeOffset End);
public sealed record ReservationView(Guid Id, Guid OperationId, Guid EngagementId, Guid ClientId, Guid ArtistId,
    DateTimeOffset Start, DateTimeOffset End, DateOnly LocalDate, string TimeZoneId, string ClientName, string ArtistName,
    string Project, Guid ProposalId, decimal ReferenceAmount, DateTimeOffset ConfirmedAt, string State = "Confirmed", string Currency = "BRL");
public sealed record Page<T>(IReadOnlyList<T> Items, string? NextCursor, bool HasMore, int Total);
public sealed record Creation<T>(T Value, bool Created);
public sealed record BusyInterval(DateTimeOffset Start, DateTimeOffset End);
public sealed record AvailabilityView(Guid ArtistId, DateOnly Date, string TimeZoneId, IReadOnlyList<Window> Windows,
    IReadOnlyList<BusyInterval> Busy, bool GuaranteesReservation = false);
public sealed record SchedulingContext(Guid Id, Guid ClientId, Guid ArtistAccountId, string State, string Project, string ArtistName,
    Guid? ProposalId, string? ProposalState, decimal? Amount, long Revision);
public interface IEngagementDirectory { Task<SchedulingContext> ReadAsync(Guid id, CancellationToken ct); }
public interface IParticipantDirectory { Task<string> NameAsync(Guid id, CancellationToken ct); }
public sealed record CalendarSnapshot(ArtistCalendar Calendar, IReadOnlyList<AvailableWindow> Windows, IReadOnlyList<Reservation> Reservations, bool HasEntries);
public interface ISchedulingStore
{
    Task<ReservationView?> FindOperationAsync(Guid artist, Guid operation, CancellationToken ct);
    Task<ReservationView?> ReadAsync(Guid id, Guid participant, CancellationToken ct);
    Task<Page<ReservationView>> ListAsync(Guid participant, string perspective, DateOnly from, DateOnly to, int limit, Guid? after, CancellationToken ct);
    Task<AvailabilityView> AvailabilityAsync(Guid artist, DateOnly date, string defaultZone, CancellationToken ct);
    Task ReplaceAvailabilityAsync(Guid artist, DateOnly date, string zone,
        Func<CalendarSnapshot, IReadOnlyList<AvailableWindow>> change, CancellationToken ct);
    Task<Creation<ReservationView>> ConfirmAsync(Guid artist, Guid operation, DateOnly date, string defaultZone, Func<ReservationView, ReservationView> repeat,
        Func<CalendarSnapshot, Reservation> create, CancellationToken ct);
}
