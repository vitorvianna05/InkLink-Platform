namespace InkLink.Scheduling.Application;

public enum SchedulingError { Invalid, Unauthenticated, Forbidden, NotFound, Conflict, Unavailable }

public sealed class SchedulingFailure(SchedulingError error, string code, Dictionary<string, string[]>? errors = null) : Exception(code)
{
    public SchedulingError Error { get; } = error;
    public string Code { get; } = code;
    public Dictionary<string, string[]> Errors { get; } = errors ?? [];
    public static SchedulingFailure Invalid(string field, string message) => new(SchedulingError.Invalid, "scheduling.invalid", new() { [field] = [message] });
}
