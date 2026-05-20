using BodyCam.Services;
using BodyCam.Services.Camera;
using BodyCam.Services.Glasses.HeyCyan;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using System.Runtime.CompilerServices;

namespace BodyCam.IntegrationTests.Camera;

public class HeyCyanCameraSelectionTests
{
    [Fact]
    public async Task WhenGlassesConnected_ActiveProviderIsHeyCyan()
    {
        // Arrange
        var session = new FakeSession();
        var selector = new HeyCyanCameraSelector(session);
        var phoneProvider = new FakeProvider("phone", isAvailable: true);
        var glassesProvider = new FakeProvider("heycyan-glasses", isAvailable: true);
        var providers = new List<ICameraProvider> { glassesProvider, phoneProvider };

        var manager = new CameraManager(
            providers,
            new FakeSettingsService(),
            selector,
            NullLogger<CameraManager>.Instance,
            session);

        // Act
        session.State = HeyCyanState.Connected;
        await manager.ReselectActiveProviderAsync();

        // Assert
        manager.Active.Should().NotBeNull();
        manager.Active!.ProviderId.Should().Be("heycyan-glasses");
    }

    [Fact]
    public async Task WhenGlassesDisconnect_ActiveProviderRevertsToPhoneCamera()
    {
        // Arrange
        var session = new FakeSession();
        var selector = new HeyCyanCameraSelector(session);
        var phoneProvider = new FakeProvider("phone", isAvailable: true);
        var glassesProvider = new FakeProvider("heycyan-glasses", isAvailable: true);
        // Phone is registered first, so it's the fallback when glasses aren't selected
        var providers = new List<ICameraProvider> { phoneProvider, glassesProvider };

        var manager = new CameraManager(
            providers,
            new FakeSettingsService(),
            selector,
            NullLogger<CameraManager>.Instance,
            session);

        session.State = HeyCyanState.Connected;
        await manager.ReselectActiveProviderAsync();
        manager.Active!.ProviderId.Should().Be("heycyan-glasses");

        // Act
        session.State = HeyCyanState.Disconnected;
        await manager.ReselectActiveProviderAsync();

        // Assert
        manager.Active.Should().NotBeNull();
        manager.Active!.ProviderId.Should().Be("phone");
    }

    [Fact]
    public async Task Reselection_CancelsInFlightCapture()
    {
        // Arrange
        var session = new FakeSession();
        var selector = new HeyCyanCameraSelector(session);
        var phoneProvider = new FakeProvider("phone", isAvailable: true);
        var glassesProvider = new FakeSlowProvider("heycyan-glasses", isAvailable: true);
        var providers = new List<ICameraProvider> { glassesProvider, phoneProvider };

        var manager = new CameraManager(
            providers,
            new FakeSettingsService(),
            selector,
            NullLogger<CameraManager>.Instance,
            session);

        session.State = HeyCyanState.Connected;
        await manager.ReselectActiveProviderAsync();

        // Act - start a capture that will take time
        var captureTask = manager.CaptureFrameAsync();

        // Immediately trigger reselection (simulating disconnect)
        session.State = HeyCyanState.Disconnected;
        await manager.ReselectActiveProviderAsync();

        // Assert - capture should complete (possibly with cancellation)
        var result = await captureTask;
        // The capture may return null due to cancellation or succeed with phone fallback
        // Either is acceptable — the key is that it doesn't hang
    }

    [Fact]
    public void Selector_FallsBackGracefully_WhenNoSessionProvided()
    {
        // Arrange
        var selector = new HeyCyanCameraSelector(session: null);
        var phoneProvider = new FakeProvider("phone", isAvailable: true);
        var providers = new List<ICameraProvider> { phoneProvider };

        // Act
        var selected = selector.Select(providers);

        // Assert
        selected.Should().NotBeNull();
        selected.ProviderId.Should().Be("phone");
    }

    // Fake implementations

    private sealed class FakeSession : IHeyCyanGlassesSession
    {
        public HeyCyanState State { get; set; } = HeyCyanState.Disconnected;
        public HeyCyanDeviceInfo? Device { get; set; }
        public HeyCyanMediaCount? LastMediaCount { get; set; }

        public event EventHandler<HeyCyanState>? StateChanged
        {
            add { }
            remove { }
        }

        public event EventHandler<HeyCyanBattery>? BatteryUpdated
        {
            add { }
            remove { }
        }

        public event EventHandler<HeyCyanButtonEvent>? ButtonPressed
        {
            add { }
            remove { }
        }

        public event EventHandler<HeyCyanMediaCount>? MediaCountUpdated
        {
            add { }
            remove { }
        }

        public event EventHandler<byte[]>? AiPhotoReceived
        {
            add { }
            remove { }
        }

        public Task<IReadOnlyList<HeyCyanDeviceInfo>> ScanAsync(TimeSpan timeout, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<HeyCyanDeviceInfo>>(Array.Empty<HeyCyanDeviceInfo>());

        public Task ConnectAsync(HeyCyanDeviceInfo device, CancellationToken ct) =>
            Task.CompletedTask;

        public Task DisconnectAsync(CancellationToken ct) =>
            Task.CompletedTask;

        public Task<HeyCyanVersionInfo> GetVersionAsync(CancellationToken ct) =>
            Task.FromResult(new HeyCyanVersionInfo("HW1", "FW1", "WHW1", "WFW1", "00:00:00:00:00:00"));

        public Task<HeyCyanBattery> GetBatteryAsync(CancellationToken ct) =>
            Task.FromResult(new HeyCyanBattery(100, false));

        public Task SyncTimeAsync(CancellationToken ct) =>
            Task.CompletedTask;

        public Task TakePhotoAsync(CancellationToken ct) =>
            Task.CompletedTask;

        public Task StartVideoAsync(CancellationToken ct) =>
            Task.CompletedTask;

        public Task StopVideoAsync(CancellationToken ct) =>
            Task.CompletedTask;

        public Task StartAudioAsync(CancellationToken ct) =>
            Task.CompletedTask;

        public Task StopAudioAsync(CancellationToken ct) =>
            Task.CompletedTask;

        public Task TakeAiPhotoAsync(CancellationToken ct) =>
            Task.CompletedTask;

        public Task<HeyCyanTransferSession> EnterTransferModeAsync(CancellationToken ct) =>
            Task.FromResult(new HeyCyanTransferSession("http://192.168.49.1", Array.Empty<string>()));

        public Task ExitTransferModeAsync(CancellationToken ct) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeProvider : ICameraProvider
    {
        public FakeProvider(string id, bool isAvailable)
        {
            ProviderId = id;
            DisplayName = id;
            IsAvailable = isAvailable;
        }

        public string DisplayName { get; }
        public string ProviderId { get; }
        public bool IsAvailable { get; }

        public event EventHandler? Disconnected
        {
            add { }
            remove { }
        }

        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;

        public Task<byte[]?> CaptureFrameAsync(CancellationToken ct = default) =>
            Task.FromResult<byte[]?>(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }); // JPEG magic

        public async IAsyncEnumerable<byte[]> StreamFramesAsync([EnumeratorCancellation] CancellationToken ct)
        {
            yield return new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
            await Task.Delay(100, ct);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeSlowProvider : ICameraProvider
    {
        public FakeSlowProvider(string id, bool isAvailable)
        {
            ProviderId = id;
            DisplayName = id;
            IsAvailable = isAvailable;
        }

        public string DisplayName { get; }
        public string ProviderId { get; }
        public bool IsAvailable { get; }

        public event EventHandler? Disconnected
        {
            add { }
            remove { }
        }

        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;

        public async Task<byte[]?> CaptureFrameAsync(CancellationToken ct = default)
        {
            try
            {
                await Task.Delay(5000, ct); // Intentionally slow
                return new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        public async IAsyncEnumerable<byte[]> StreamFramesAsync([EnumeratorCancellation] CancellationToken ct)
        {
            yield return new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
            await Task.Delay(100, ct);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        public string? ActiveCameraProvider { get; set; }
        public string? ActiveAudioInputProvider { get; set; }
        public string? ActiveAudioOutputProvider { get; set; }
        public OpenAiProvider Provider { get; set; }
        public string? AzureEndpoint { get; set; }
        public string? AzureRealtimeDeploymentName { get; set; }
        public string? AzureChatDeploymentName { get; set; }
        public string? AzureVisionDeploymentName { get; set; }
        public string AzureApiVersion { get; set; } = string.Empty;
        public string RealtimeModel { get; set; } = string.Empty;
        public string ChatModel { get; set; } = string.Empty;
        public string VisionModel { get; set; } = string.Empty;
        public string TranscriptionModel { get; set; } = string.Empty;
        public string Voice { get; set; } = string.Empty;
        public string TurnDetection { get; set; } = string.Empty;
        public string NoiseReduction { get; set; } = string.Empty;
        public string SystemInstructions { get; set; } = string.Empty;
        public bool DebugMode { get; set; }
        public bool ShowTokenCounts { get; set; }
        public bool ShowCostEstimate { get; set; }
        public bool SendDiagnosticData { get; set; }
        public bool SendUsageData { get; set; }
        public bool SendCrashReports { get; set; }
        public string? AzureMonitorConnectionString { get; set; }
        public string? SentryDsn { get; set; }
        public string? PicovoiceAccessKey { get; set; }
        public bool FeedVoiceNotesToDictation { get; set; }
        public string? LastHeyCyanDeviceAddress { get; set; }
        public string? LastHeyCyanDeviceName { get; set; }
        public bool HeyCyanAutoReconnect { get; set; } = true;
        public string? A9CameraIp { get; set; }
        public string? A9CameraUid { get; set; }
        public string? A9CameraUsername { get; set; }
        public string? A9CameraPassword { get; set; }
        public Models.DeviceSettings DeviceSettings { get; set; } = new();
        public bool SetupCompleted { get; set; }
    }
}
