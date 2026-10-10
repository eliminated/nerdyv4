using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Nerdy.App.Services;

/// <summary>Minimal daily-rolling file logger (%LOCALAPPDATA%\Nerdy\logs) so problems can be diagnosed on users' PCs.</summary>
public sealed class FileLoggerProvider(string directory) : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();
    private readonly object _gate = new();

    public ILogger CreateLogger(string categoryName) => _loggers.GetOrAdd(categoryName, c => new FileLogger(this, c));

    public void Dispose() => _loggers.Clear();

    private void Write(string line)
    {
        var path = Path.Combine(directory, $"nerdy-{DateTime.Now:yyyyMMdd}.log");
        lock (_gate)
        {
            try { File.AppendAllText(path, line + Environment.NewLine); }
            catch (IOException) { /* logging must never crash the app */ }
        }
    }

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        private readonly string _shortCategory = category[(category.LastIndexOf('.') + 1)..];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var line = $"{DateTime.Now:HH:mm:ss.fff} [{logLevel.ToString()[..4].ToUpperInvariant()}] {_shortCategory}: {formatter(state, exception)}";
            if (exception is not null) line += Environment.NewLine + exception;
            provider.Write(line);
        }
    }
}
