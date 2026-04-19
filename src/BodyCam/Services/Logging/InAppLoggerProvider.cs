using Microsoft.Extensions.Logging;

namespace BodyCam.Services.Logging;

public class InAppLoggerProvider : ILoggerProvider
{
    private readonly InAppLogSink _sink;
    private readonly LogLevel _minimumLevel;

    public InAppLoggerProvider(InAppLogSink sink, LogLevel minimumLevel = LogLevel.Debug)
    {
        _sink = sink;
        _minimumLevel = minimumLevel;
    }

    public ILogger CreateLogger(string categoryName) =>
        new InAppLogger(categoryName, _sink, _minimumLevel);

    public void Dispose() { }

    private sealed class InAppLogger(string category, InAppLogSink sink, LogLevel minimumLevel) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= minimumLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var message = formatter(state, exception);
            sink.Add(new InAppLogEntry(DateTimeOffset.Now, logLevel, category, message, exception));
        }
    }
}
