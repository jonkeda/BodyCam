using BodyCam.Orchestration;
using BodyCam.Services;
using BodyCam.Services.Audio;
using BodyCam.Services.Camera;
using BodyCam.Services.Camera.Commands;
using BodyCam.Services.Glasses;
using BodyCam.Services.Glasses.HeyCyan;
using BodyCam.Services.QrCode;
using BodyCam.Tests.Services.Glasses.HeyCyan.Fakes;
using BodyCam.ViewModels;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace BodyCam.Tests.ViewModels;

public sealed class MainViewModelCaptureBusyStateTests
{
    [Fact]
    public async Task PhotoCommand_WhenManualCaptureCompletes_HidesPreviewAndShowsBusyDots()
    {
        var manualCapture = new ManualCameraCaptureCoordinator();
        var commandService = new PendingManualCommandService(manualCapture);
        var vm = CreateVm(commandService, manualCapture);

        vm.LookCommand.Execute(null);
        await commandService.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => vm.ShowInlineCameraPreview);

        vm.PhotoCommand.Execute(null);

        await commandService.CaptureCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() =>
            !vm.ShowInlineCameraPreview
            && vm.ShowTranscriptTab
            && vm.Entries.Any(entry =>
                entry.Role == "AI"
                && entry.IsThinking
                && IsBusyDot(entry.Text)));

        var aiEntry = vm.Entries.Single(entry => entry.Role == "AI");

        aiEntry.IsThinking.Should().BeTrue();
        IsBusyDot(aiEntry.Text).Should().BeTrue();
        aiEntry.AccessibleText.Should().Be("AI is thinking");

        commandService.Finish.SetResult();
        await commandService.Returned.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => !aiEntry.IsThinking);

        aiEntry.IsThinking.Should().BeFalse(
            $"the command should have replaced the busy row; current text is '{aiEntry.Text}'");
        aiEntry.Text.Should().Be("Manual answer.");
        vm.Entries.Count(entry => entry.Role == "AI").Should().Be(1);
        vm.Entries.Count(entry => entry.Role == "You").Should().Be(1);
        vm.Entries.IndexOf(vm.Entries.Single(entry => entry.Role == "You"))
            .Should()
            .BeLessThan(vm.Entries.IndexOf(aiEntry));
    }

    private static bool IsBusyDot(string value) =>
        value is "." or ".." or "...";

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
        {
            await Task.Delay(20, cts.Token);
        }
    }

    private static MainViewModel CreateVm(
        ICameraCommandService commandService,
        IManualCameraCaptureCoordinator manualCapture)
    {
        var settings = new FakeSettingsService
        {
            DefaultTouchCommandMode = CameraCommandMode.ManualAim,
            DefaultLookDetailLevel = LookDetailLevel.Overview,
        };

        var cameraManager = new CameraManager(
            [],
            settings,
            new DefaultCameraSelector(),
            NullLogger<CameraManager>.Instance,
            null);

        return new MainViewModel(
            new StubOrchestrator(),
            Substitute.For<IApiKeyService>(),
            settings,
            cameraManager,
            Substitute.For<IQrCodeScanner>(),
            new QrCodeService(),
            new QrContentResolver([]),
            CreateGlassesManager(settings),
            NullLogger<MainViewModel>.Instance,
            cameraCommands: commandService,
            manualCapture: manualCapture);
    }

    private static HeyCyanGlassesDeviceManager CreateGlassesManager(ISettingsService settings)
    {
        var session = new FakeHeyCyanSessionWithVersion();
        var transfer = new FakeHeyCyanMediaTransfer();
        var btInput = new FakeBluetoothAudioInputProvider(["AA:BB:CC:DD:EE:FF"]);
        var btOutput = new FakeBluetoothAudioOutputProvider(["AA:BB:CC:DD:EE:FF"]);

        var camera = new HeyCyanCameraProvider(
            session,
            transfer,
            NullLogger<HeyCyanCameraProvider>.Instance,
            photoSettleDelay: TimeSpan.Zero);

        var mic = new HeyCyanAudioInputProvider(
            session,
            btInput,
            NullLogger<HeyCyanAudioInputProvider>.Instance);

        var speaker = new HeyCyanAudioOutputProvider(
            session,
            btOutput,
            NullLogger<HeyCyanAudioOutputProvider>.Instance);

        var button = new HeyCyanButtonProvider(
            session,
            NullLogger<HeyCyanButtonProvider>.Instance);

        return new HeyCyanGlassesDeviceManager(
            session,
            camera,
            mic,
            speaker,
            button,
            transfer,
            settings,
            NullLogger<HeyCyanGlassesDeviceManager>.Instance);
    }

    private sealed class PendingManualCommandService : ICameraCommandService
    {
        private readonly IManualCameraCaptureCoordinator _manualCapture;

        public PendingManualCommandService(IManualCameraCaptureCoordinator manualCapture)
        {
            _manualCapture = manualCapture;
        }

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource CaptureCompleted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Finish { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Returned { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<CameraCommandResult> ExecuteAsync(
            CameraCommandRequest request,
            CancellationToken ct = default)
        {
            Started.TrySetResult();

            await _manualCapture.WaitForCaptureAsync(
                request,
                _ => Task.FromResult<byte[]?>([0xFF, 0xD8]),
                ct);

            CaptureCompleted.TrySetResult();
            await Finish.Task.WaitAsync(ct);

            var result = new CameraCommandResult(
                request.CommandId,
                Success: true,
                TranscriptText: "Manual answer.",
                Data: new Dictionary<string, object?>
                {
                    ["mode"] = CameraCommandMode.ManualAim.ToString(),
                },
                Error: null,
                TranscriptInput: new CameraCommandTranscriptInput(
                    "Look. Give an overview.",
                    ImageBytes: null,
                    "Captured frame"));

            Returned.TrySetResult();
            return result;
        }
    }

    private sealed class StubOrchestrator : AgentOrchestrator
    {
#pragma warning disable CS8618
        public StubOrchestrator()
            : base(null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!)
        {
        }
#pragma warning restore CS8618
    }
}
