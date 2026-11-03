using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace InkLink.Messaging.Api.Extensions;

internal static class ConversationTelemetry
{
    public const string Name = "InkLink.Messaging.Events";
    public static readonly ActivitySource Activities = new(Name);
    private static readonly Meter Meter = new(Name);
    private static readonly Counter<long> Deliveries = Meter.CreateCounter<long>("inklink.messaging.deliveries");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("inklink.messaging.processing.duration", "s");
    public static void Record(string outcome, long started)
    {
        var tags = new TagList { { "outcome", outcome } };
        Deliveries.Add(1, tags);
        Duration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds, tags);
    }
}
