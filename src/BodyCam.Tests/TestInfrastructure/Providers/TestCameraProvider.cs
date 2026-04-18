using System.Runtime.CompilerServices;
using BodyCam.Services.Camera;

namespace BodyCam.Tests.TestInfrastructure.Providers;

public class TestCameraProvider : ICameraProvider
{
    private readonly byte[][] _frames;
    private int _frameIndex;

    public string DisplayName => "Test Camera";
    public string ProviderId => "test-camera";
    public bool IsAvailable { get; set; } = true;

    public event EventHandler? Disconnected;

    public int FramesCaptured { get; private set; }

    public TestCameraProvider(byte[] singleFrame)
    {
        _frames = [singleFrame];
    }

    public TestCameraProvider(params byte[][] frames)
    {
        _frames = frames;
    }

    public TestCameraProvider(string framesDirectory)
    {
        var files = Directory.GetFiles(framesDirectory, "*.jpg").OrderBy(f => f).ToArray();
        _frames = files.Select(File.ReadAllBytes).ToArray();
    }

    public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;

    public Task<byte[]?> CaptureFrameAsync(CancellationToken ct = default)
    {
        if (_frames.Length == 0 || !IsAvailable)
            return Task.FromResult<byte[]?>(null);

        var frame = _frames[_frameIndex % _frames.Length];
        _frameIndex++;
        FramesCaptured++;
        return Task.FromResult<byte[]?>(frame);
    }

    public async IAsyncEnumerable<byte[]> StreamFramesAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested && IsAvailable)
        {
            if (_frames.Length == 0) yield break;
            var frame = _frames[_frameIndex % _frames.Length];
            _frameIndex++;
            FramesCaptured++;
            yield return frame;
            await Task.Delay(100, ct).ConfigureAwait(false);
        }
    }

    public void SimulateDisconnect()
    {
        IsAvailable = false;
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    public void Reset()
    {
        _frameIndex = 0;
        FramesCaptured = 0;
        IsAvailable = true;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
