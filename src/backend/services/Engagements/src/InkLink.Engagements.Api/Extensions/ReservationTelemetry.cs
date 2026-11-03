using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace InkLink.Engagements.Api.Extensions;

internal static class ReservationTelemetry
{
    public const string Name = "InkLink.Engagements.Reservations";
    public static readonly ActivitySource Activities = new(Name);
    private static readonly Meter Meter = new(Name);
    private static readonly Counter<long> Deliveries = Meter.CreateCounter<long>("inklink.engagements.reservations.deliveries");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("inklink.engagements.reservations.processing.duration", "s");
    public static void Record(string outcome, long started)
    {
        var tags = new TagList { { "outcome", outcome } };
        Deliveries.Add(1, tags);
        Duration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds, tags);
    }
}
