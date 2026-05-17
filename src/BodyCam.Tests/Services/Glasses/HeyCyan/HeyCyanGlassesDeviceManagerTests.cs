using BodyCam.Services.Glasses;
using BodyCam.Services.Glasses.HeyCyan;
using BodyCam.Tests.Services.Glasses.HeyCyan.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BodyCam.Tests.Services.Glasses.HeyCyan;

/// <summary>
/// Unit tests for <see cref="HeyCyanGlassesDeviceManager"/> — M33 Phase 7 Wave 1.
/// </summary>
public sealed class HeyCyanGlassesDeviceManagerTests
{
    [Fact]
    public async Task Connect_PopulatesStatus()
    {
        // Arrange
        var session = new FakeHeyCyanSessionWithVersion();
        var (camera, mic, speaker, button, media, log) = CreateDependencies(session);
        var sut = new HeyCyanGlassesDeviceManager(session, camera, mic, speaker, button, media, log);

        var device = new HeyCyanDeviceInfo("TestGlasses", "AA:BB:CC:DD:EE:FF", -50);

        // Act
        await sut.ConnectAsync(device, CancellationToken.None);

        // Assert
        sut.Version.Should().NotBeNull();
        sut.Version!.MacAddress.Should().Be("AA:BB:CC:DD:EE:FF");
        sut.Battery.Should().NotBeNull();
        sut.Battery!.Percentage.Should().Be(85);
        sut.MacAddress.Should().Be("AA:BB:CC:DD:EE:FF");
    }

    [Theory]
    [InlineData(HeyCyanState.Disconnected, GlassesConnectionState.Disconnected)]
    [InlineData(HeyCyanState.Scanning, GlassesConnectionState.Scanning)]
    [InlineData(HeyCyanState.Connecting, GlassesConnectionState.Connecting)]
    [InlineData(HeyCyanState.Connected, GlassesConnectionState.Connected)]
    [InlineData(HeyCyanState.TransferMode, GlassesConnectionState.Connected)]
    [InlineData(HeyCyanState.Disconnecting, GlassesConnectionState.Disconnecting)]
    public void SessionStateChanged_MapsToGlassesState(
        HeyCyanState sessionState,
        GlassesConnectionState expectedGlassesState)
    {
        // Arrange
        var session = new FakeHeyCyanSessionWithVersion();
        var (camera, mic, speaker, button, media, log) = CreateDependencies(session);
        var sut = new HeyCyanGlassesDeviceManager(session, camera, mic, speaker, button, media, log);

        GlassesConnectionState? capturedState = null;
        sut.StateChanged += (_, state) => capturedState = state;

        // Act
        session.RaiseStateChanged(sessionState);

        // Assert
        sut.State.Should().Be(expectedGlassesState);
        capturedState.Should().Be(expectedGlassesState);
    }

    [Fact]
    public void BatteryUpdated_UpdatesBatteryAndRaisesStatusChanged()
    {
        // Arrange
        var session = new FakeHeyCyanSessionWithVersion();
        var (camera, mic, speaker, button, media, log) = CreateDependencies(session);
        var sut = new HeyCyanGlassesDeviceManager(session, camera, mic, speaker, button, media, log);

        var statusChangedFired = false;
        sut.StatusChanged += (_, _) => statusChangedFired = true;

        var newBattery = new HeyCyanBattery(42, IsCharging: true);

        // Act
        session.RaiseBatteryUpdated(newBattery);

        // Assert
        sut.Battery.Should().Be(newBattery);
        sut.Battery!.Percentage.Should().Be(42);
        sut.Battery.IsCharging.Should().BeTrue();
        statusChangedFired.Should().BeTrue();
    }

    [Fact]
    public void MediaCountUpdated_UpdatesMediaCountAndRaisesStatusChanged()
    {
        // Arrange
        var session = new FakeHeyCyanSessionWithVersion();
        var (camera, mic, speaker, button, media, log) = CreateDependencies(session);
        var sut = new HeyCyanGlassesDeviceManager(session, camera, mic, speaker, button, media, log);

        var statusChangedFired = false;
        sut.StatusChanged += (_, _) => statusChangedFired = true;

        var newMediaCount = new HeyCyanMediaCount(Photos: 5, Videos: 3, AudioFiles: 2);

        // Act
        session.RaiseMediaCountUpdated(newMediaCount);

        // Assert
        sut.MediaCount.Should().Be(newMediaCount);
        sut.MediaCount!.Photos.Should().Be(5);
        sut.MediaCount.Videos.Should().Be(3);
        sut.MediaCount.AudioFiles.Should().Be(2);
        statusChangedFired.Should().BeTrue();
    }

    [Fact]
    public async Task ScanAsync_ReturnsDeviceListFromSession()
    {
        // Arrange
        var session = new FakeHeyCyanSessionWithVersion();
        var (camera, mic, speaker, button, media, log) = CreateDependencies(session);
        var sut = new HeyCyanGlassesDeviceManager(session, camera, mic, speaker, button, media, log);

        // Act
        var devices = await sut.ScanAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

        // Assert
        devices.Should().NotBeNull();
        devices.Should().HaveCount(2);
        devices[0].Name.Should().Be("TestGlasses1");
        devices[1].Name.Should().Be("TestGlasses2");
    }

    // Helper to create fake dependencies
    private static (
        HeyCyanCameraProvider camera,
        HeyCyanAudioInputProvider mic,
        HeyCyanAudioOutputProvider speaker,
        HeyCyanButtonProvider button,
        IHeyCyanMediaTransfer media,
        ILogger<HeyCyanGlassesDeviceManager> log)
        CreateDependencies(IHeyCyanGlassesSession session)
    {
        var fakeTransfer = new FakeHeyCyanMediaTransfer();
        var fakeBtInput = new FakeBluetoothAudioInputProvider(new[] { "AA:BB:CC:DD:EE:FF" });
        var fakeBtOutput = new FakeBluetoothAudioOutputProvider(new[] { "AA:BB:CC:DD:EE:FF" });

        var camera = new HeyCyanCameraProvider(
            session,
            fakeTransfer,
            NullLogger<HeyCyanCameraProvider>.Instance);

        var mic = new HeyCyanAudioInputProvider(
            session,
            fakeBtInput,
            NullLogger<HeyCyanAudioInputProvider>.Instance);

        var speaker = new HeyCyanAudioOutputProvider(
            session,
            fakeBtOutput,
            NullLogger<HeyCyanAudioOutputProvider>.Instance);

        var button = new HeyCyanButtonProvider(
            session,
            NullLogger<HeyCyanButtonProvider>.Instance);

        var log = NullLogger<HeyCyanGlassesDeviceManager>.Instance;

        return (camera, mic, speaker, button, fakeTransfer, log);
    }
}
