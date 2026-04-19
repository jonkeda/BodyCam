using Microsoft.Extensions.Logging;

namespace BodyCam.Services.Logging;

public record InAppLogEntry(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Category,
    string Message,
    Exception? Exception);
