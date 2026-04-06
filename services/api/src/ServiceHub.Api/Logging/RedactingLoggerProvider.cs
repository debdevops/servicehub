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
        // Return a no-op disposable rather than null. Callers use `using var scope = ...`
        // and will null-dereference on dispose if null is returned. The NullScope pattern
        // is the same approach used by Microsoft.Extensions.Logging.Abstractions.
        return NullScope.Instance;
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

    /// <summary>
    /// A no-op <see cref="IDisposable"/> returned from <see cref="BeginScope{TState}"/>.
    /// </summary>
    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        private NullScope() { }
        public void Dispose() { }
    }
}
