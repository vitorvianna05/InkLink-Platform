namespace InkLink.Scheduling.Domain;

public sealed class ClosureConflict(string code) : Exception(code);
public static class ReservationClosure
{
    public static void EnsureAllowed(bool futureReservations, bool mismatchedParticipants)
    {
        if (mismatchedParticipants) throw new ClosureConflict("scheduling.participants_mismatch");
        if (futureReservations) throw new ClosureConflict("scheduling.future_reservations");
    }
}
