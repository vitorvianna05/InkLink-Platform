using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;

namespace InkLink.Identity.Infrastructure.Messaging;

/// <summary>Rastreamento e métricas da publicação do Outbox da Identity (padrão de P05).</summary>
public static class EventTelemetry
{
    public const string Name = "InkLink.Identity.Events";
    public static readonly ActivitySource Activities = new(Name);
    private static readonly Meter Meter = new(Name);
    private static readonly Counter<long> Publications = Meter.CreateCounter<long>("inklink.identity.publications");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("inklink.identity.publication.duration", "s");
    private static readonly Histogram<double> Age = Meter.CreateHistogram<double>("inklink.identity.outbox.age", "s");

    /// <summary>Continua o rastreamento da requisição que gravou o evento (traceParent do envelope).</summary>
    public static Activity? Start(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var parent = document.RootElement.TryGetProperty("traceParent", out var trace) && trace.ValueKind == JsonValueKind.String ? trace.GetString() : null;
        ActivityContext.TryParse(parent, null, isRemote: true, out var context);
        return Activities.StartActivity("profile-photo.publish", ActivityKind.Producer, context);
    }

    public static void Record(string outcome, long started, DateTimeOffset created, DateTimeOffset now)
    {
        var tags = new TagList { { "outcome", outcome } };
        Publications.Add(1, tags);
        Duration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds, tags);
        Age.Record(Math.Max(0, (now - created).TotalSeconds), tags);
    }
}
