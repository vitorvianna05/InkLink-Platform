using System.Diagnostics;
namespace InkLink.Engagements.Application.Lifecycle;

public sealed record LifecycleCorrelation(string? TraceParent, string? CorrelationId)
{
    public static LifecycleCorrelation Capture(string? traceParent = null, string? correlationId = null)
    {
        var parent = Activity.Current?.Id ?? traceParent;
        if (!ActivityContext.TryParse(parent, null, out var context)) parent = null;
        else parent = $"00-{context.TraceId}-{context.SpanId}-{(byte)context.TraceFlags:x2}";
        return new(parent, correlationId ?? Activity.Current?.TraceId.ToString());
    }
}
