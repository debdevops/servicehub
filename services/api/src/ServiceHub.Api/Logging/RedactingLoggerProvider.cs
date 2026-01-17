using Microsoft.Extensions.Logging;
using ServiceHub.Infrastructure.Security;

namespace ServiceHub.Api.Logging;

/// <summary>
/// A logging provider that redacts sensitive information from all log messages.
/// </summary>
public sealed class RedactingLoggerProvider : ILoggerProvider
{
    private bool _disposed = false;

    /// <summary>
    /// Initializes a new instance of the <see cref="RedactingLoggerProvider"/> class.
    /// </summary>
    public RedactingLoggerProvider()
    {
    }

    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(RedactingLoggerProvider));
        
        return new RedactingLogger(categoryName);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;
        
        _disposed = true;
    }
}

/// <summary>
/// A logger that redacts sensitive information from log messages and state.
/// </summary>
public sealed class RedactingLogger : ILogger
{
    private readonly string _categoryName;

    /// <summary>
    /// Initializes a new instance of the <see cref="RedactingLogger"/> class.
    /// </summary>
    /// <param name="categoryName">The logger category name.</param>
    public RedactingLogger(string categoryName)
    {
        _categoryName = categoryName;
    }

    /// <inheritdoc/>
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return null; // No scope support for simplicity
    }

    /// <inheritdoc/>
    public bool IsEnabled(LogLevel logLevel)
    {
        // Log at Information level and above by default
        return logLevel >= LogLevel.Information;
    }

    /// <inheritdoc/>
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        // Redact the formatted message
        var originalMessage = formatter(state, exception);
        var redactedMessage = LogRedactor.Redact(originalMessage);

        // Format and output the log message
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var levelString = logLevel.ToString().ToUpperInvariant();
        var exceptionInfo = exception != null ? $"\n{exception}" : string.Empty;

        var logOutput = $"[{timestamp}] [{levelString}] [{_categoryName}] {redactedMessage}{exceptionInfo}";
        
        Console.WriteLine(logOutput);
    }
}
