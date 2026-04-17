using BodyCam.Services;

namespace BodyCam.Agents;

/// <summary>
/// Sends camera frames to GPT-5.4 Vision for scene understanding.
/// </summary>
public class VisionAgent
{
    private readonly ICameraService _camera;
    private readonly AppSettings _settings;

    public VisionAgent(ICameraService camera, AppSettings settings)
    {
        _camera = camera;
        _settings = settings;
    }

    public Task<string> DescribeFrameAsync(byte[] jpegFrame, CancellationToken ct = default)
    {
        // TODO: Send frame to GPT-5.4 Vision endpoint (M3)
        return Task.FromResult("[VisionAgent stub] No description available.");
    }

    public async Task<string?> CaptureAndDescribeAsync(CancellationToken ct = default)
    {
        var frame = await _camera.CaptureFrameAsync(ct);
        if (frame is null) return null;
        return await DescribeFrameAsync(frame, ct);
    }
}
