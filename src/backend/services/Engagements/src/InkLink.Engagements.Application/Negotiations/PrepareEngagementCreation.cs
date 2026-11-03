using InkLink.Engagements.Domain;
namespace InkLink.Engagements.Application.Negotiations;

public static class PrepareEngagementCreation
{
    public static void Apply(Engagement engagement, IReadOnlyList<EngagementState> previousStates)
    {
        if (previousStates.Any(state => state is EngagementState.Closed or EngagementState.Closing))
            engagement.RequireReservation();
    }
}
