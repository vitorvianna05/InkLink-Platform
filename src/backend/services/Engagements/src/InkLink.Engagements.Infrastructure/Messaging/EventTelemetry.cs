using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;

namespace InkLink.Engagements.Infrastructure.Messaging;

public static class EventTelemetry
{
    public const string Name = "InkLink.Engagements.Events";
    public static readonly ActivitySource Activities = new(Name);
    private static readonly Meter Meter = new(Name);
    private static readonly Counter<long> Publications = Meter.CreateCounter<long>("inklink.engagements.publications");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("inklink.engagements.publication.duration", "s");
    private static readonly Histogram<double> Age = Meter.CreateHistogram<double>("inklink.engagements.outbox.age", "s");
    public static Activity? Start(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var parent = document.RootElement.TryGetProperty("traceParent", out var trace) ? trace.GetString() : null;
        ActivityContext.TryParse(parent, null, out var context);
        return Activities.StartActivity("engagement.publish", ActivityKind.Producer, context);
    }
    public static void Record(string outcome, long started, DateTimeOffset created)
    {
        var tags = new TagList { { "outcome", outcome } };
        Publications.Add(1, tags);
        Duration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds, tags);
        Age.Record(Math.Max(0, (DateTimeOffset.UtcNow - created).TotalSeconds), tags);
    }
}
