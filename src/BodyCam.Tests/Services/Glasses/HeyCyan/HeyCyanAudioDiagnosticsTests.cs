using BodyCam.Services.Glasses.HeyCyan;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace BodyCam.Tests.Services.Glasses.HeyCyan;

/// <summary>
/// Unit tests for <see cref="HeyCyanAudioDiagnostics"/>.
/// </summary>
public sealed class HeyCyanAudioDiagnosticsTests : IAsyncDisposable
{
    private readonly FakeHeyCyanGlassesSession _session = new();
    private readonly FakeCodecProbe _probe = new();
    private readonly HeyCyanAudioDiagnostics _sut;

    public HeyCyanAudioDiagnosticsTests()
    {
        _sut = new HeyCyanAudioDiagnostics(_session, _probe, NullLogger<HeyCyanAudioDiagnostics>.Instance);
    }

    [Fact]
    public void Current_IsNullInitially()
    {
        _sut.Current.Should().BeNull();
    }

    [Fact]
    public async Task RefreshAsync_WithNoDevice_SetCurrentToNull()
    {
        // Arrange: session has no device
        _session.Device = null;

        // Act
        await _sut.RefreshAsync();

        // Assert
        _sut.Current.Should().BeNull();
    }

    [Fact]
    public async Task RefreshAsync_WithDevice_CallsProbe()
    {
        // Arrange
        var device = new HeyCyanDeviceInfo("Test Glasses", "AA:BB:CC:DD:EE:FF", -50);
        _session.Device = device;

        var expectedInfo = new HeyCyanAudioRouteInfo(
            "heycyan-glasses",
            "heycyan-glasses",
            "SBC",
            44100,
            2,
            "CVSD");
        _probe.NextResult = expectedInfo;

        // Act
        await _sut.RefreshAsync();

        // Assert
        _probe.LastMac.Should().Be("AA:BB:CC:DD:EE:FF");
        _sut.Current.Should().Be(expectedInfo);
    }

    [Fact]
    public async Task RefreshAsync_RaisesUpdated_WhenProbeSucceeds()
    {
        // Arrange
        var device = new HeyCyanDeviceInfo("Test Glasses", "AA:BB:CC:DD:EE:FF", -50);
        _session.Device = device;

        var expectedInfo = new HeyCyanAudioRouteInfo(
            "heycyan-glasses",
            "heycyan-glasses",
            "AAC",
            48000,
            2,
            "mSBC");
        _probe.NextResult = expectedInfo;

        HeyCyanAudioRouteInfo? receivedInfo = null;
        _sut.Updated += (_, info) => receivedInfo = info;

        // Act
        await _sut.RefreshAsync();

        // Assert
        receivedInfo.Should().Be(expectedInfo);
    }

    [Fact]
    public async Task RefreshAsync_DoesNotRaiseUpdated_WhenProbeReturnsNull()
    {
        // Arrange
        var device = new HeyCyanDeviceInfo("Test Glasses", "AA:BB:CC:DD:EE:FF", -50);
        _session.Device = device;
        _probe.NextResult = null;

        var updateRaised = false;
        _sut.Updated += (_, _) => updateRaised = true;

        // Act
        await _sut.RefreshAsync();

        // Assert
        updateRaised.Should().BeFalse();
        _sut.Current.Should().BeNull();
    }

    [Fact]
    public async Task RefreshAsync_CatchesExceptions_SetsCurrentToNull()
    {
        // Arrange
        var device = new HeyCyanDeviceInfo("Test Glasses", "AA:BB:CC:DD:EE:FF", -50);
        _session.Device = device;
        _probe.ShouldThrow = true;

        // Act
        await _sut.RefreshAsync();

        // Assert
        _sut.Current.Should().BeNull();
    }

    [Fact]
    public async Task StateChanged_ToConnected_TriggersRefresh()
    {
        // Arrange
        var device = new HeyCyanDeviceInfo("Test Glasses", "AA:BB:CC:DD:EE:FF", -50);
        _session.Device = device;

        var expectedInfo = new HeyCyanAudioRouteInfo(
            "heycyan-glasses",
            "heycyan-glasses",
            "aptX",
            48000,
            2,
            null);
        _probe.NextResult = expectedInfo;

        // Act
        _session.RaiseStateChanged(HeyCyanState.Connected);

        // Wait for async event handler
        await Task.Delay(50);

        // Assert
        _sut.Current.Should().Be(expectedInfo);
    }

    [Fact]
    public async Task StateChanged_ToDisconnected_DoesNotTriggerRefresh()
    {
        // Arrange
        var device = new HeyCyanDeviceInfo("Test Glasses", "AA:BB:CC:DD:EE:FF", -50);
        _session.Device = device;
        _probe.NextResult = new HeyCyanAudioRouteInfo("heycyan-glasses", "heycyan-glasses", "SBC", 44100, 2, null);

        // Pre-populate Current
        await _sut.RefreshAsync();
        _probe.CallCount = 0;

        // Act
        _session.RaiseStateChanged(HeyCyanState.Disconnected);
        await Task.Delay(50);

        // Assert
        _probe.CallCount.Should().Be(0, "refresh should not be called on disconnect");
    }

    public async ValueTask DisposeAsync()
    {
        await _sut.DisposeAsync();
        await _session.DisposeAsync();
    }

    /// <summary>
    /// Fake codec probe for testing.
    /// </summary>
    private sealed class FakeCodecProbe : IHeyCyanCodecProbe
    {
        public string? LastMac { get; private set; }
        public int CallCount { get; set; }
        public HeyCyanAudioRouteInfo? NextResult { get; set; }
        public bool ShouldThrow { get; set; }

        public Task<HeyCyanAudioRouteInfo?> ProbeAsync(string mac, CancellationToken ct)
        {
            CallCount++;
            LastMac = mac;

            if (ShouldThrow)
                throw new InvalidOperationException("Probe failed.");

            return Task.FromResult(NextResult);
        }
    }

    /// <summary>
    /// Minimal fake session for testing diagnostics.
    /// </summary>
    private sealed class FakeHeyCyanGlassesSession : IHeyCyanGlassesSession
    {
        public HeyCyanState State { get; set; } = HeyCyanState.Disconnected;
        public HeyCyanDeviceInfo? Device { get; set; }
        public HeyCyanMediaCount? LastMediaCount => null;

        public event EventHandler<HeyCyanState>? StateChanged;
        public event EventHandler<HeyCyanBattery>? BatteryUpdated { add { } remove { } }
        public event EventHandler<HeyCyanButtonEvent>? ButtonPressed { add { } remove { } }
        public event EventHandler<HeyCyanMediaCount>? MediaCountUpdated { add { } remove { } }
        public event EventHandler<byte[]>? AiPhotoReceived { add { } remove { } }

        public void RaiseStateChanged(HeyCyanState newState)
        {
            State = newState;
            StateChanged?.Invoke(this, newState);
        }

        public Task<IReadOnlyList<HeyCyanDeviceInfo>> ScanAsync(TimeSpan timeout, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<HeyCyanDeviceInfo>>([]);

        public Task ConnectAsync(HeyCyanDeviceInfo device, CancellationToken ct)
            => Task.CompletedTask;

        public Task DisconnectAsync(CancellationToken ct)
            => Task.CompletedTask;

        public Task<HeyCyanVersionInfo> GetVersionAsync(CancellationToken ct)
            => Task.FromResult(new HeyCyanVersionInfo("1.0", "1.0", "1.0", "1.0", "AA:BB:CC:DD:EE:FF"));

        public Task<HeyCyanBattery> GetBatteryAsync(CancellationToken ct)
            => Task.FromResult(new HeyCyanBattery(100, false));

        public Task SyncTimeAsync(CancellationToken ct)
            => Task.CompletedTask;

        public Task TakePhotoAsync(CancellationToken ct)
            => Task.CompletedTask;

        public Task StartVideoAsync(CancellationToken ct)
            => Task.CompletedTask;

        public Task StopVideoAsync(CancellationToken ct)
            => Task.CompletedTask;

        public Task StartAudioAsync(CancellationToken ct)
            => Task.CompletedTask;

        public Task StopAudioAsync(CancellationToken ct)
            => Task.CompletedTask;

        public Task TakeAiPhotoAsync(CancellationToken ct)
            => Task.CompletedTask;

        public Task<HeyCyanTransferSession> EnterTransferModeAsync(CancellationToken ct)
            => Task.FromResult(new HeyCyanTransferSession("http://192.168.49.1", []));

        public Task ExitTransferModeAsync(CancellationToken ct)
            => Task.CompletedTask;

        public ValueTask DisposeAsync()
            => default;
    }
}
