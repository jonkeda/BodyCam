using BodyCam.Models;
using BodyCam.Services;

namespace BodyCam.Tools;

public sealed class ToolContext
{
    public required Func<CancellationToken, Task<byte[]?>> CaptureFrame { get; init; }
    public required SessionContext Session { get; init; }
    public required Action<string> Log { get; init; }
    public required IRealtimeClient RealtimeClient { get; init; }
}
