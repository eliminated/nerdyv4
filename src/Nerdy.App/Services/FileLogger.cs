using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Nerdy.App.Services;

/// <summary>
/// Minimal daily-rolling file logger (%LOCALAPPDATA%\Nerdy\logs) so problems can be diagnosed on users' PCs.
/// Files older than <see cref="KeepFor"/> are deleted when a new day's file starts.
/// </summary>
public sealed class FileLoggerProvider(string directory) : ILoggerProvider
{
    /// <summary>How long log files are kept (the privacy policy in site/privacy promises this).</summary>
    public static readonly TimeSpan KeepFor = TimeSpan.FromDays(14);

    private const string FilePattern = "nerdy-*.log";

    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();
    private readonly object _gate = new();
    private string? _currentPath;

    public ILogger CreateLogger(string categoryName) => _loggers.GetOrAdd(categoryName, c => new FileLogger(this, c));

    public void Dispose() => _loggers.Clear();

    /// <summary>Deletes every log file, e.g. after the account they mention was deleted.</summary>
    public static void DeleteLogs(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory, FilePattern)) File.Delete(file);
    }

    private void Write(string line)
    {
        var path = Path.Combine(directory, $"nerdy-{DateTime.Now:yyyyMMdd}.log");
        lock (_gate)
        {
            try
            {
                if (path != _currentPath)
                {
                    _currentPath = path;
                    DeleteOldLogs();
                }
                File.AppendAllText(path, line + Environment.NewLine);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* logging must never crash the app */ }
        }
    }

    private void DeleteOldLogs()
    {
        var cutoff = DateTime.Now - KeepFor;
        foreach (var file in Directory.EnumerateFiles(directory, FilePattern))
            if (File.GetLastWriteTime(file) < cutoff) File.Delete(file);
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
