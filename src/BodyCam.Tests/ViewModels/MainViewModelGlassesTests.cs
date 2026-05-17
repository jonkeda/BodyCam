using BodyCam.Orchestration;
using BodyCam.Services;
using BodyCam.Services.Audio;
using BodyCam.Services.Camera;
using BodyCam.Services.Glasses;
using BodyCam.Services.Glasses.HeyCyan;
using BodyCam.Services.QrCode;
using BodyCam.Tests.Services.Glasses.HeyCyan.Fakes;
using BodyCam.ViewModels;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace BodyCam.Tests.ViewModels;

/// <summary>
/// Unit tests for MainViewModel glasses status projection (M33 Phase 7 Wave 3).
/// Tests the shell battery widget properties (GlassesConnected, GlassesBatteryPct, etc.).
/// </summary>
public sealed class MainViewModelGlassesTests
{
    private readonly FakeHeyCyanSessionWithVersion _session;
    private readonly HeyCyanGlassesDeviceManager _glasses;

    public MainViewModelGlassesTests()
    {
        _session = new FakeHeyCyanSessionWithVersion();
        _glasses = CreateManager(_session);
    }

    private MainViewModel CreateVm()
    {
        // MainViewModel constructor subscribes to orchestrator events, so we need a stub
        var orchestrator = new StubOrchestrator();
        var apiKeyService = Substitute.For<IApiKeyService>();
        var settingsService = new FakeSettingsService();
        var cameraManager = CreateMinimalCameraManager(settingsService);
        var qrScanner = Substitute.For<IQrCodeScanner>();
        var qrCodeService = CreateMinimalQrCodeService();
        var contentResolver = CreateMinimalContentResolver();
        var logger = NullLogger<MainViewModel>.Instance;

        return new MainViewModel(
            orchestrator,
            apiKeyService,
            settingsService,
            cameraManager,
            qrScanner,
            qrCodeService,
            contentResolver,
            _glasses,
            logger);
    }

    // Minimal orchestrator stub for testing MainViewModel
    private sealed class StubOrchestrator : AgentOrchestrator
    {
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value
        public StubOrchestrator()
            : base(null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!)
        {
        }
#pragma warning restore CS8618
    }

    private static CameraManager CreateMinimalCameraManager(ISettingsService settings)
    {
        var selector = new DefaultCameraSelector();
        return new CameraManager([], settings, selector, NullLogger<CameraManager>.Instance, null);
    }

    private static QrCodeService CreateMinimalQrCodeService()
    {
        return new QrCodeService();
    }

    private static QrContentResolver CreateMinimalContentResolver()
    {
        return new QrContentResolver([]);
    }

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

        return new HeyCyanGlassesDeviceManager(session, camera, mic, speaker, button, fakeTransfer, new FakeSettingsService(), log);
    }

    [Fact]
    public void GlassesConnected_ReturnsFalseWhenDisconnected()
    {
        _session.RaiseStateChanged(HeyCyanState.Disconnected);
        var vm = CreateVm();

        vm.GlassesConnected.Should().BeFalse();
    }

    [Fact]
    public void GlassesConnected_ReturnsTrueWhenConnected()
    {
        _session.RaiseStateChanged(HeyCyanState.Connected);
        var vm = CreateVm();

        vm.GlassesConnected.Should().BeTrue();
    }

    [Fact]
    public void GlassesBatteryPct_ReturnsPercentageFromBattery()
    {
        _session.RaiseBatteryUpdated(new HeyCyanBattery(75, IsCharging: false));
        var vm = CreateVm();

        vm.GlassesBatteryPct.Should().Be(75);
    }

    [Fact]
    public void GlassesBatteryPct_ReturnsZeroWhenBatteryIsNull()
    {
        var vm = CreateVm();

        vm.GlassesBatteryPct.Should().Be(0);
    }

    [Fact]
    public void GlassesCharging_ReturnsTrueWhenCharging()
    {
        _session.RaiseBatteryUpdated(new HeyCyanBattery(50, IsCharging: true));
        var vm = CreateVm();

        vm.GlassesCharging.Should().BeTrue();
    }

    [Fact]
    public void GlassesCharging_ReturnsFalseWhenNotCharging()
    {
        _session.RaiseBatteryUpdated(new HeyCyanBattery(50, IsCharging: false));
        var vm = CreateVm();

        vm.GlassesCharging.Should().BeFalse();
    }

    [Fact]
    public void GlassesCharging_ReturnsFalseWhenBatteryIsNull()
    {
        var vm = CreateVm();

        vm.GlassesCharging.Should().BeFalse();
    }

    [Fact]
    public void GlassesBatteryPct_UpdatesOnStatusChanged()
    {
        var vm = CreateVm();
        vm.GlassesBatteryPct.Should().Be(0); // Initial state

        _session.RaiseBatteryUpdated(new HeyCyanBattery(42, IsCharging: false));

        vm.GlassesBatteryPct.Should().Be(42);
    }

    [Fact]
    public void GlassesBatteryColor_IsRedWhenLowAndNotCharging()
    {
        _session.RaiseBatteryUpdated(new HeyCyanBattery(10, IsCharging: false));
        var vm = CreateVm();

        vm.GlassesBatteryColor.Should().Be(Colors.Red);
    }

    [Fact]
    public void GlassesBatteryColor_IsWhiteWhenLowButCharging()
    {
        _session.RaiseBatteryUpdated(new HeyCyanBattery(10, IsCharging: true));
        var vm = CreateVm();

        vm.GlassesBatteryColor.Should().Be(Colors.White);
    }

    [Fact]
    public void GlassesBatteryColor_IsWhiteWhenNotLow()
    {
        _session.RaiseBatteryUpdated(new HeyCyanBattery(50, IsCharging: false));
        var vm = CreateVm();

        vm.GlassesBatteryColor.Should().Be(Colors.White);
    }

    [Fact]
    public void NavigateToGlassesCommand_IsNotNull()
    {
        var vm = CreateVm();

        vm.NavigateToGlassesCommand.Should().NotBeNull();
    }
}
