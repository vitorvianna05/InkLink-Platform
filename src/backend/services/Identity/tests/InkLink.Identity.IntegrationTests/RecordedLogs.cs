using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace InkLink.Identity.IntegrationTests;

internal sealed record RecordedLog(string Category, string Message, IReadOnlyDictionary<string, object?> State, IReadOnlyDictionary<string, object?> Scope);

internal sealed class RecordedLogs : ILoggerProvider, ISupportExternalScope
{
    private IExternalScopeProvider scopeProvider = new LoggerExternalScopeProvider();
    public ConcurrentQueue<RecordedLog> Entries { get; } = new();
    public ILogger CreateLogger(string categoryName) => new Recorder(this, categoryName);
    public void SetScopeProvider(IExternalScopeProvider provider) => scopeProvider = provider;
    public void Dispose() { }

    private sealed class Recorder(RecordedLogs owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => owner.scopeProvider.Push(state);
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var fields = state is IEnumerable<KeyValuePair<string, object?>> pairs ? pairs.ToDictionary(pair => pair.Key, pair => pair.Value) : new();
            var scopes = new Dictionary<string, object?>();
            owner.scopeProvider.ForEachScope((scope, values) =>
            {
                if (scope is IEnumerable<KeyValuePair<string, object?>> properties)
                    foreach (var (key, value) in properties) values[key] = value;
            }, scopes);
            owner.Entries.Enqueue(new(category, formatter(state, exception), fields, scopes));
        }
    }
}
