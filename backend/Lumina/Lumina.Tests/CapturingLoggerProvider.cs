using Microsoft.Extensions.Logging;

namespace Lumina.Tests;

internal sealed record CapturedLog(LogLevel Level, string Category, string Message,
    IReadOnlyDictionary<string, object?> Properties);

internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly List<CapturedLog> _entries = [];
    public IReadOnlyList<CapturedLog> Entries { get { lock (_entries) return _entries.ToArray(); } }
    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);
    public void Dispose() { }

    private sealed class CapturingLogger(string category, List<CapturedLog> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? pairs.ToDictionary(pair => pair.Key, pair => pair.Value)
                : new Dictionary<string, object?>();
            lock (entries) entries.Add(new CapturedLog(logLevel, category, formatter(state, exception), properties));
        }
    }
}
