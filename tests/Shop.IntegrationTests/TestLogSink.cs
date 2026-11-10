using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Shop.IntegrationTests;

/// <summary>
/// B5: a log sink the test host installs so tests can assert what IS logged —
/// chiefly ADR-0002's rule that /pay/* tokens never reach request logs.
/// </summary>
public sealed class TestLogSink : ILoggerProvider
{
    public static readonly TestLogSink Instance = new();
    public readonly ConcurrentQueue<string> Messages = new();

    public ILogger CreateLogger(string categoryName) => new SinkLogger(Messages);

    public void Dispose() { }

    private sealed class SinkLogger(ConcurrentQueue<string> messages) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => messages.Enqueue(formatter(state, exception));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
