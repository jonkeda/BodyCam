using BodyCam.Services.Glasses;
using BodyCam.Services.Glasses.HeyCyan;
using BodyCam.Tests.Services.Glasses.HeyCyan.Fakes;
using BodyCam.ViewModels;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace BodyCam.Tests.ViewModels;

/// <summary>
/// Unit tests for <see cref="GlassesViewModel"/> — M33 Phase 7 Wave 2.
/// </summary>
public sealed class GlassesViewModelTests
{
    private readonly FakeHeyCyanSessionWithVersion _session;
    private readonly HeyCyanGlassesDeviceManager _glasses;

    public GlassesViewModelTests()
    {
        _session = new FakeHeyCyanSessionWithVersion();
        _glasses = CreateManager(_session);
    }

    private GlassesViewModel CreateVm() => new(_glasses);

    private static HeyCyanGlassesDeviceManager CreateManager(IHeyCyanGlassesSession session)
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

        return new HeyCyanGlassesDeviceManager(session, camera, mic, speaker, button, fakeTransfer, log);
    }

    [Fact]
    public void Constructor_InitializesCommands()
    {
        var vm = CreateVm();

        vm.ScanCommand.Should().NotBeNull();
        vm.ConnectCommand.Should().NotBeNull();
        vm.DisconnectCommand.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_InitializesDevicesCollection()
    {
        var vm = CreateVm();

        vm.Devices.Should().NotBeNull();
        vm.Devices.Should().BeEmpty();
    }

    [Theory]
    [InlineData(GlassesConnectionState.Disconnected, "Not connected")]
    [InlineData(GlassesConnectionState.Scanning, "Scanning…")]
    [InlineData(GlassesConnectionState.Connecting, "Connecting…")]
    public void StatusText_ReflectsState(GlassesConnectionState state, string expectedText)
    {
        var heyCyanState = state switch
        {
            GlassesConnectionState.Disconnected => HeyCyanState.Disconnected,
            GlassesConnectionState.Scanning => HeyCyanState.Scanning,
            GlassesConnectionState.Connecting => HeyCyanState.Connecting,
            _ => HeyCyanState.Disconnected
        };
        _session.RaiseStateChanged(heyCyanState);
        var vm = CreateVm();

        vm.StatusText.Should().Be(expectedText);
    }

    [Fact]
    public void StatusText_WhenConnected_ShowsBatteryPercent()
    {
        _session.RaiseStateChanged(HeyCyanState.Connected);
        _session.RaiseBatteryUpdated(new HeyCyanBattery(75, IsCharging: false));
        var vm = CreateVm();

        vm.StatusText.Should().Be("Connected — 75%");
    }

    [Fact]
    public void StatusText_WhenConnectedAndCharging_ShowsLightningBolt()
    {
        _session.RaiseStateChanged(HeyCyanState.Connected);
        _session.RaiseBatteryUpdated(new HeyCyanBattery(42, IsCharging: true));
        var vm = CreateVm();

        vm.StatusText.Should().Be("Connected — 42% ⚡");
    }

    [Fact]
    public void IsConnected_ReturnsTrueWhenStateIsConnected()
    {
        _session.RaiseStateChanged(HeyCyanState.Connected);
        var vm = CreateVm();

        vm.IsConnected.Should().BeTrue();
    }

    [Fact]
    public void IsConnected_ReturnsFalseWhenStateIsDisconnected()
    {
        _session.RaiseStateChanged(HeyCyanState.Disconnected);
        var vm = CreateVm();

        vm.IsConnected.Should().BeFalse();
    }

    [Fact]
    public void BatteryPct_ReturnsPercentageFromBattery()
    {
        _session.RaiseBatteryUpdated(new HeyCyanBattery(65, IsCharging: false));
        var vm = CreateVm();

        vm.BatteryPct.Should().Be(65);
    }

    [Fact]
    public void BatteryPct_ReturnsZeroWhenBatteryIsNull()
    {
        // Fresh session has no battery data
        var vm = CreateVm();

        vm.BatteryPct.Should().Be(0);
    }

    [Fact]
    public void IsCharging_ReturnsTrueWhenBatteryIsCharging()
    {
        _session.RaiseBatteryUpdated(new HeyCyanBattery(50, IsCharging: true));
        var vm = CreateVm();

        vm.IsCharging.Should().BeTrue();
    }

    [Fact]
    public void IsCharging_ReturnsFalseWhenBatteryIsNull()
    {
        var vm = CreateVm();

        vm.IsCharging.Should().BeFalse();
    }

    [Fact]
    public async Task Mac_ReturnsMacAddressFromVersion()
    {
        var device = new HeyCyanDeviceInfo("TestGlasses", "AA:BB:CC:DD:EE:FF", -50);
        await _glasses.ConnectAsync(device, CancellationToken.None);
        var vm = CreateVm();

        vm.Mac.Should().Be("AA:BB:CC:DD:EE:FF");
    }

    [Fact]
    public void Mac_ReturnsPlaceholderWhenVersionIsNull()
    {
        var vm = CreateVm();

        vm.Mac.Should().Be("—");
    }

    [Fact]
    public async Task Firmware_ReturnsFirmwareFromVersion()
    {
        var device = new HeyCyanDeviceInfo("TestGlasses", "AA:BB:CC:DD:EE:FF", -50);
        await _glasses.ConnectAsync(device, CancellationToken.None);
        var vm = CreateVm();

        vm.Firmware.Should().NotBe("—");
    }

    [Fact]
    public void Firmware_ReturnsPlaceholderWhenVersionIsNull()
    {
        var vm = CreateVm();

        vm.Firmware.Should().Be("—");
    }

    [Fact]
    public async Task Hardware_ReturnsHardwareFromVersion()
    {
        var device = new HeyCyanDeviceInfo("TestGlasses", "AA:BB:CC:DD:EE:FF", -50);
        await _glasses.ConnectAsync(device, CancellationToken.None);
        var vm = CreateVm();

        vm.Hardware.Should().NotBe("—");
    }

    [Fact]
    public void Hardware_ReturnsPlaceholderWhenVersionIsNull()
    {
        var vm = CreateVm();

        vm.Hardware.Should().Be("—");
    }

    [Fact]
    public void Photos_ReturnsPhotosFromMediaCount()
    {
        _session.RaiseMediaCountUpdated(new HeyCyanMediaCount(Photos: 10, Videos: 5, AudioFiles: 3));
        var vm = CreateVm();

        vm.Photos.Should().Be(10);
    }

    [Fact]
    public void Photos_ReturnsZeroWhenMediaCountIsNull()
    {
        var vm = CreateVm();

        vm.Photos.Should().Be(0);
    }

    [Fact]
    public void Videos_ReturnsVideosFromMediaCount()
    {
        _session.RaiseMediaCountUpdated(new HeyCyanMediaCount(Photos: 10, Videos: 7, AudioFiles: 3));
        var vm = CreateVm();

        vm.Videos.Should().Be(7);
    }

    [Fact]
    public void Videos_ReturnsZeroWhenMediaCountIsNull()
    {
        var vm = CreateVm();

        vm.Videos.Should().Be(0);
    }

    [Fact]
    public void AudioFiles_ReturnsAudioFilesFromMediaCount()
    {
        _session.RaiseMediaCountUpdated(new HeyCyanMediaCount(Photos: 10, Videos: 5, AudioFiles: 8));
        var vm = CreateVm();

        vm.AudioFiles.Should().Be(8);
    }

    [Fact]
    public void AudioFiles_ReturnsZeroWhenMediaCountIsNull()
    {
        var vm = CreateVm();

        vm.AudioFiles.Should().Be(0);
    }

    [Fact]
    public void ConnectCommand_CannotExecuteWhenNoDeviceSelected()
    {
        var vm = CreateVm();
        vm.SelectedDevice = null;

        vm.ConnectCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void ConnectCommand_CanExecuteWhenDeviceSelected()
    {
        var vm = CreateVm();
        vm.SelectedDevice = new HeyCyanDeviceInfo("TestGlasses", "AA:BB:CC", -60);

        vm.ConnectCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void DisconnectCommand_CannotExecuteWhenDisconnected()
    {
        _session.RaiseStateChanged(HeyCyanState.Disconnected);
        var vm = CreateVm();

        vm.DisconnectCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void DisconnectCommand_CanExecuteWhenConnected()
    {
        var vm = CreateVm();
        _session.RaiseStateChanged(HeyCyanState.Connected);

        vm.DisconnectCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void StateChanged_UpdatesAllStatusProperties()
    {
        _session.RaiseBatteryUpdated(new HeyCyanBattery(80, IsCharging: true));
        _session.RaiseMediaCountUpdated(new HeyCyanMediaCount(Photos: 12, Videos: 8, AudioFiles: 5));

        var vm = CreateVm();

        var changedProperties = new List<string>();
        vm.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName!);

        // Act
        _session.RaiseStateChanged(HeyCyanState.Connected);

        // Assert
        changedProperties.Should().Contain(nameof(vm.IsConnected));
        changedProperties.Should().Contain(nameof(vm.StatusText));
    }

    [Fact]
    public void StatusChanged_UpdatesAllStatusProperties()
    {
        var vm = CreateVm();

        var changedProperties = new List<string>();
        vm.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName!);

        // Act
        _session.RaiseBatteryUpdated(new HeyCyanBattery(90, IsCharging: false));

        // Assert
        changedProperties.Should().Contain(nameof(vm.BatteryPct));
        changedProperties.Should().Contain(nameof(vm.IsCharging));
    }

    [Fact]
    public async Task ScanAsync_PopulatesDevicesCollection()
    {
        var vm = CreateVm();

        // Act
        await vm.ScanCommand.ExecuteAsync(null);

        // Assert
        vm.Devices.Should().HaveCount(2);
        vm.Devices[0].Name.Should().Be("TestGlasses1");
        vm.Devices[1].Name.Should().Be("TestGlasses2");
    }

    [Fact]
    public async Task ScanAsync_ClearsExistingDevices()
    {
        var vm = CreateVm();
        vm.Devices.Add(new HeyCyanDeviceInfo("OldGlasses", "AA:BB:CC", -50));

        // Act
        await vm.ScanCommand.ExecuteAsync(null);

        // Assert
        vm.Devices.Should().HaveCount(2);
        vm.Devices[0].Name.Should().Be("TestGlasses1");
    }

    [Fact]
    public void IsScanning_DefaultsToFalse()
    {
        var vm = CreateVm();

        vm.IsScanning.Should().BeFalse();
    }

    [Fact]
    public async Task ScanAsync_SetsIsScanningTrueDuringScan()
    {
        _session.ScanCompletionSource = new TaskCompletionSource<IReadOnlyList<HeyCyanDeviceInfo>>();
        var vm = CreateVm();

        var scanTask = Task.Run(() => vm.ScanCommand.Execute(null));
        await Task.Delay(50); // Let the command start

        vm.IsScanning.Should().BeTrue();

        // Complete the scan so the task finishes
        _session.ScanCompletionSource.SetResult(new List<HeyCyanDeviceInfo>());
        await scanTask;

        vm.IsScanning.Should().BeFalse();
    }

    [Fact]
    public async Task StopScanCommand_CancelsScanAndSetsIsScanningFalse()
    {
        _session.ScanCompletionSource = new TaskCompletionSource<IReadOnlyList<HeyCyanDeviceInfo>>();
        var vm = CreateVm();

        var scanTask = Task.Run(() => vm.ScanCommand.Execute(null));
        await Task.Delay(50); // Let the command start

        vm.IsScanning.Should().BeTrue();

        // Act — cancel
        vm.StopScanCommand.Execute(null);
        await scanTask;

        // Assert
        vm.IsScanning.Should().BeFalse();
        vm.Devices.Should().BeEmpty();
    }

    [Fact]
    public async Task ScanAsync_NotifiesIsScanningPropertyChanged()
    {
        var vm = CreateVm();
        var changedProperties = new List<string>();
        vm.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName!);

        // Act
        await vm.ScanCommand.ExecuteAsync(null);

        // Assert — should have toggled IsScanning true then false
        changedProperties.Should().Contain(nameof(vm.IsScanning));
    }
}

// Extension method to simplify async command execution in tests
file static class AsyncRelayCommandExtensions
{
    public static async Task ExecuteAsync(this BodyCam.Mvvm.AsyncRelayCommand command, object? parameter)
    {
        command.Execute(parameter);
        // Wait a bit for the async operation to complete
        await Task.Delay(50);
    }
}
