namespace InkLink.Catalog.Application;

public enum CatalogError { Invalid, Unauthenticated, Forbidden, NotFound, Conflict, Unavailable }

public sealed class CatalogFailure(CatalogError error, string code, Dictionary<string, string[]>? errors = null) : Exception(code)
{
    public CatalogError Error { get; } = error;
    public string Code { get; } = code;
    public Dictionary<string, string[]> Errors { get; } = errors ?? [];
    public static CatalogFailure Invalid(string field, string message) => new(CatalogError.Invalid, "catalog.invalid", new() { [field] = [message] });
}
