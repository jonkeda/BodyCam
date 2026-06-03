using System.Runtime.CompilerServices;
using BodyCam.Services.Camera;
using BodyCam.Tests.Services.Glasses.HeyCyan.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace BodyCam.Tests.Services.Camera;

public sealed class CameraManagerTests
{
    [Fact]
    public async Task SetActiveAsync_WhenProviderStartFails_ContinuesWithoutActiveCamera()
    {
        var settings = new FakeSettingsService
        {
            ActiveCameraProvider = "previous-camera",
        };
        var provider = new ThrowingCameraProvider("failing-camera");
        var manager = new CameraManager(
            [provider],
            settings,
            new DefaultCameraSelector(),
            NullLogger<CameraManager>.Instance);

        await manager.SetActiveAsync(provider.ProviderId);

        manager.Active.Should().BeNull();
        settings.ActiveCameraProvider.Should().Be("previous-camera");
        provider.StopCount.Should().Be(1);
    }

    private sealed class ThrowingCameraProvider : ICameraProvider
    {
        public ThrowingCameraProvider(string providerId)
        {
            ProviderId = providerId;
        }

        public string DisplayName => "Failing Camera";

        public string ProviderId { get; }

        public bool IsAvailable => false;

        public bool SupportsVideoRecording => false;

        public int StopCount { get; private set; }

        public event EventHandler? Disconnected;

        public Task StartAsync(CancellationToken ct = default)
            => throw new InvalidOperationException("camera hardware unavailable");

        public Task StopAsync()
        {
            StopCount++;
            return Task.CompletedTask;
        }

        public Task<byte[]?> CaptureFrameAsync(CancellationToken ct = default)
            => Task.FromResult<byte[]?>(null);

        public async IAsyncEnumerable<byte[]> StreamFramesAsync(
            [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            yield break;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
