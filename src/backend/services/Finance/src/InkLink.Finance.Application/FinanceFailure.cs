namespace InkLink.Finance.Application;

public enum FinanceError { Invalid, Unauthenticated, Forbidden, NotFound, Conflict, Unavailable }

public sealed class FinanceFailure(FinanceError error, string code, Dictionary<string, string[]>? errors = null) : Exception(code)
{
    public FinanceError Error { get; } = error;
    public string Code { get; } = code;
    public Dictionary<string, string[]> Errors { get; } = errors ?? [];
    public static FinanceFailure Invalid(string field, string message) => new(FinanceError.Invalid, "finance.invalid", new() { [field] = [message] });
}
