namespace InkLink.Engagements.Application;

public enum EngagementsError { Invalid, Unauthenticated, Forbidden, NotFound, Conflict, Unavailable }

public sealed class EngagementsFailure(EngagementsError error, string code, Dictionary<string, string[]>? errors = null) : Exception(code)
{
    public EngagementsError Error { get; } = error;
    public string Code { get; } = code;
    public Dictionary<string, string[]> Errors { get; } = errors ?? [];
    public static EngagementsFailure Invalid(string field, string message) => new(EngagementsError.Invalid, "engagements.invalid", new() { [field] = [message] });
}
