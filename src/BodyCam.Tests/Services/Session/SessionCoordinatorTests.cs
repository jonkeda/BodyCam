using BodyCam.Services;
using BodyCam.Services.Session;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace BodyCam.Tests.Services.Session;

public sealed class SessionCoordinatorTests
{
    [Fact]
    public async Task SetLayerAsync_ActiveSession_PromptsForMissingApiKeyAndStartsRuntime()
    {
        var runtime = new FakeSessionRuntime();
        var keys = new FakeApiKeyService();
        var coordinator = CreateCoordinator(runtime, keys);

        var result = await coordinator.SetLayerAsync(
            SessionLayer.ActiveSession,
            new SessionTransitionOptions(
                PromptForApiKeyAsync: () => Task.FromResult<string?>("test-key"),
                FrameCaptureFunc: _ => Task.FromResult<byte[]?>([1, 2, 3])));

        result.Success.Should().BeTrue();
        result.CurrentLayer.Should().Be(SessionLayer.ActiveSession);
        result.IsRunning.Should().BeTrue();
        result.ToggleButtonText.Should().Be("Stop");
        runtime.StartCount.Should().Be(1);
        runtime.StopListeningCount.Should().Be(1);
        runtime.FrameCaptureFunc.Should().NotBeNull();
        keys.StoredKey.Should().Be("test-key");
    }

    [Fact]
    public async Task SetLayerAsync_Sleep_FromActiveSession_StopsRuntimeAndClearsFrameCapture()
    {
        var runtime = new FakeSessionRuntime { Running = true };
        var keys = new FakeApiKeyService { StoredKey = "test-key" };
        var coordinator = CreateCoordinator(runtime, keys);

        await coordinator.SetLayerAsync(
            SessionLayer.ActiveSession,
            new SessionTransitionOptions(FrameCaptureFunc: _ => Task.FromResult<byte[]?>([1])));

        var result = await coordinator.SetLayerAsync(SessionLayer.Sleep);

        result.CurrentLayer.Should().Be(SessionLayer.Sleep);
        result.IsRunning.Should().BeFalse();
        result.ToggleButtonText.Should().Be("Start");
        runtime.StopCount.Should().Be(1);
        runtime.StopListeningCount.Should().Be(2);
        runtime.FrameCaptureFunc.Should().BeNull();
    }

    [Fact]
    public async Task SetLayerAsync_WakeWord_StartsListeningWithoutRuntimeSession()
    {
        var coordinator = CreateCoordinator(new FakeSessionRuntime(), new FakeApiKeyService());

        var result = await coordinator.SetLayerAsync(SessionLayer.WakeWord);

        result.Success.Should().BeTrue();
        result.CurrentLayer.Should().Be(SessionLayer.WakeWord);
        result.IsRunning.Should().BeFalse();
        result.StatusText.Should().Be("Listening...");
    }

    [Fact]
    public async Task SetLayerAsync_ActiveSession_WithoutApiKeyPrompt_ReturnsApiKeyRequired()
    {
        var runtime = new FakeSessionRuntime();
        var coordinator = CreateCoordinator(runtime, new FakeApiKeyService());

        var result = await coordinator.SetLayerAsync(SessionLayer.ActiveSession);

        result.Success.Should().BeFalse();
        result.CurrentLayer.Should().Be(SessionLayer.Sleep);
        result.StatusText.Should().Be("API key required");
        runtime.StartCount.Should().Be(0);
    }

    private static SessionCoordinator CreateCoordinator(
        ISessionRuntime runtime,
        IApiKeyService apiKeyService) =>
        new(runtime, apiKeyService, NullLogger<SessionCoordinator>.Instance);

    private sealed class FakeSessionRuntime : ISessionRuntime
    {
        public bool Running { get; set; }
        public bool IsRunning => Running;
        public Func<CancellationToken, Task<byte[]?>>? FrameCaptureFunc { get; set; }
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }
        public int StartListeningCount { get; private set; }
        public int StopListeningCount { get; private set; }

        public Task StartAsync()
        {
            StartCount++;
            Running = true;
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            StopCount++;
            Running = false;
            return Task.CompletedTask;
        }

        public Task StartListeningAsync()
        {
            StartListeningCount++;
            return Task.CompletedTask;
        }

        public Task StopListeningAsync()
        {
            StopListeningCount++;
            return Task.CompletedTask;
        }

        public Task StopSpeakingAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task SendTextInputAsync(string text, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeApiKeyService : IApiKeyService
    {
        public string? StoredKey { get; set; }
        public bool HasKey => !string.IsNullOrWhiteSpace(StoredKey);

        public Task<string?> GetApiKeyAsync() => Task.FromResult(StoredKey);
        public Task<string?> GetApiKeyAsync(string providerId) => Task.FromResult(StoredKey);

        public Task SetApiKeyAsync(string apiKey)
        {
            StoredKey = apiKey;
            return Task.CompletedTask;
        }

        public Task SetApiKeyAsync(string providerId, string apiKey) => SetApiKeyAsync(apiKey);

        public Task ClearApiKeyAsync()
        {
            StoredKey = null;
            return Task.CompletedTask;
        }

        public Task ClearApiKeyAsync(string providerId) => ClearApiKeyAsync();
    }
}
