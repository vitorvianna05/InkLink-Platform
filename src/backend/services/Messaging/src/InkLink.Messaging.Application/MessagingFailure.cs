namespace InkLink.Messaging.Application;

public enum MessagingError { Invalid, Unauthenticated, Forbidden, NotFound, Conflict, Unavailable }

public sealed class MessagingFailure(MessagingError error, string code, Dictionary<string, string[]>? errors = null) : Exception(code)
{
    public MessagingError Error { get; } = error;
    public string Code { get; } = code;
    public Dictionary<string, string[]> Errors { get; } = errors ?? [];
    public static MessagingFailure Invalid(string field, string message) => new(MessagingError.Invalid, "messaging.invalid", new() { [field] = [message] });
}
