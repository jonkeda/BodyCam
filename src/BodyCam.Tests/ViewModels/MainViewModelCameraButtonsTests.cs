using BodyCam.Orchestration;
using BodyCam.Models;
using BodyCam.Services;
using BodyCam.Services.Actions;
using BodyCam.Services.Audio;
using BodyCam.Services.Barcode;
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
using System.Runtime.CompilerServices;

namespace BodyCam.Tests.ViewModels;

public sealed class MainViewModelCameraButtonsTests
{
    [Fact]
    public void Constructor_BuildsCameraActionsFromRegisteredActions()
    {
        var vm = CreateVm([
            RecordingCommand.Look(),
            RecordingCommand.Read(),
            RecordingCommand.Single("scan", "Scan"),
            RecordingCommand.Single("hidden", "Hidden")
        ],
        actions:
        [
            CameraAction(AssistiveActionIds.Look, "Look", "look"),
            CameraAction(AssistiveActionIds.Find, "Find", "look"),
            CameraAction(AssistiveActionIds.Read, "Read", "read"),
            CameraAction(AssistiveActionIds.Scan, "Scan", "scan"),
            new(
                AssistiveActionIds.Photo,
                "Photo",
                RequiresCamera: true,
                StartsOrStopsSession: false),
            new(
                AssistiveActionIds.ToggleSession,
                "Toggle session",
                RequiresCamera: true,
                StartsOrStopsSession: true)
        ]);

        vm.CameraActions.Select(action => action.Label)
            .Should()
            .Equal("Look", "Find", "Read", "Scan");
        vm.HasCameraActions.Should().BeTrue();
        vm.ShowCameraActionRail.Should().BeFalse();
        vm.ShowCameraActionsSection.Should().BeFalse();
        vm.ActiveCameraAction.Should().BeNull();
        vm.ActiveCameraActionVariants.Should().BeEmpty();
        vm.HasActiveCameraActionVariants.Should().BeFalse();

        vm.ShowInlineCameraPreview = true;
        vm.ShowCameraActionRail.Should().BeTrue();
    }

    [Fact]
    public async Task ActivateCameraActionAsync_ShowsGeneratedVariantsAndPreview()
    {
        var vm = CreateVm([RecordingCommand.Look()]);

        await vm.ActivateCameraActionAsync(vm.CameraActions.Single());

        vm.ShowInlineCameraPreview.Should().BeTrue();
        vm.ShowCameraActionsSection.Should().BeTrue();
        vm.ShowCameraActionRail.Should().BeFalse();
        vm.ActiveCameraAction.Should().Be(vm.CameraActions.Single());
        vm.ActiveCameraAction!.IsActive.Should().BeTrue();
        vm.ActiveCameraActionVariants.Select(variant => variant.Label)
            .Should()
            .Equal("Overview", "Summary", "Detail");
        vm.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task ActivateCameraActionAsync_StartsLiveScanWhenNoCameraProviderIsActiveYet()
    {
        var provider = new FixedFrameCameraProvider([0xFF, 0xD8, 0x11]);
        var settings = new FakeSettingsService();
        var cameraManager = new CameraManager(
            [provider],
            settings,
            new DefaultCameraSelector(),
            NullLogger<CameraManager>.Instance,
            null);
        var liveScanner = new RecordingLiveBarcodeScanner();
        var vm = CreateVm(
            [RecordingCommand.Single("scan", "Scan")],
            actions: [CameraAction(AssistiveActionIds.Scan, "Scan", "scan")],
            settings: settings,
            cameraManager: cameraManager,
            liveBarcodeScanner: liveScanner,
            useCameraActionFrameCapture: false);

        await vm.ActivateCameraActionAsync(vm.CameraActions.Single());

        await liveScanner.WatchStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        liveScanner.WatchCalls.Should().Be(1);
        provider.StartCount.Should().Be(1);
        cameraManager.Active.Should().BeSameAs(provider);
    }

    [Fact]
    public async Task ActivateCameraActionAsync_AutoProductScan_ClosesCameraActionSurface()
    {
        var provider = new FixedFrameCameraProvider([0xFF, 0xD8, 0x12]);
        var settings = new FakeSettingsService();
        var cameraManager = new CameraManager(
            [provider],
            settings,
            new DefaultCameraSelector(),
            NullLogger<CameraManager>.Instance,
            null);
        var liveScanner = new DetectionLiveBarcodeScanner(
            new LiveBarcodeDetection("4006420012345", QrCodeFormat.Ean13, DateTimeOffset.UtcNow));
        var product = new ProductInfo
        {
            Barcode = "4006420012345",
            Source = "test",
            Name = "Mineral Water"
        };
        var workflow = new FixedProductLookupWorkflow(new ProductBarcodeLookupResult(
            ProductBarcodeLookupStatus.Found,
            product.Barcode,
            product,
            product.Name,
            QrCodeFormat.Ean13));
        var vm = CreateVm(
            [RecordingCommand.Single("scan", "Scan")],
            actions: [CameraAction(AssistiveActionIds.Scan, "Scan", "scan")],
            settings: settings,
            cameraManager: cameraManager,
            liveBarcodeScanner: liveScanner,
            productLookupWorkflow: workflow,
            useCameraActionFrameCapture: false);

        await vm.ActivateCameraActionAsync(vm.CameraActions.Single());

        await WaitUntilAsync(() => !vm.ShowInlineCameraPreview);

        AssertCameraActionSelectionCleared(vm);
        vm.ShowCameraActionsSection.Should().BeFalse();
        vm.Entries.Should().ContainSingle(entry => entry.Role == "Product" && entry.Text == "Mineral Water");
        workflow.Calls.Should().Be(1);
    }

    [Fact]
    public async Task ActivateCameraActionAsync_ReopensScanAfterAutoScanClosesSurface()
    {
        var provider = new FixedFrameCameraProvider([0xFF, 0xD8, 0x13]);
        var settings = new FakeSettingsService();
        var cameraManager = new CameraManager(
            [provider],
            settings,
            new DefaultCameraSelector(),
            NullLogger<CameraManager>.Instance,
            null);
        var liveScanner = new ReopenLiveBarcodeScanner(
            new LiveBarcodeDetection("https://example.com", QrCodeFormat.QrCode, DateTimeOffset.UtcNow));
        var vm = CreateVm(
            [RecordingCommand.Single("scan", "Scan")],
            actions: [CameraAction(AssistiveActionIds.Scan, "Scan", "scan")],
            settings: settings,
            cameraManager: cameraManager,
            liveBarcodeScanner: liveScanner,
            useCameraActionFrameCapture: false);
        var scanAction = vm.CameraActions.Single();

        await vm.ActivateCameraActionAsync(scanAction);

        await WaitUntilAsync(() => !vm.ShowInlineCameraPreview);
        AssertCameraActionSelectionCleared(vm);

        await vm.ActivateCameraActionAsync(scanAction);
        await liveScanner.SecondWatchStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        vm.ShowInlineCameraPreview.Should().BeTrue();
        vm.ShowCameraActionsSection.Should().BeTrue();
        vm.ShowCameraActionRail.Should().BeFalse();
        vm.ActiveCameraAction.Should().Be(scanAction);
        scanAction.IsActive.Should().BeTrue();
        vm.ActiveCameraActionVariants.Should().ContainSingle(variant => variant.Label == "Scan");
        vm.HasActiveCameraActionVariants.Should().BeTrue();
        liveScanner.WatchCalls.Should().Be(2);
    }

    [Fact]
    public async Task ActivateCameraActionAsync_ShowsVariantsForEveryRegisteredAction()
    {
        var vm = CreateVm(
            [
                RecordingCommand.Look(),
                RecordingCommand.Read(),
                RecordingCommand.Single("scan", "Scan")
            ],
            actions:
            [
                CameraAction(AssistiveActionIds.Look, "Look", "look"),
                CameraAction(AssistiveActionIds.Find, "Find", "look"),
                CameraAction(AssistiveActionIds.Read, "Read", "read"),
                CameraAction(AssistiveActionIds.Scan, "Scan", "scan")
            ]);

        var expected = new Dictionary<string, string[]>
        {
            ["Look"] = ["Overview", "Summary", "Detail"],
            ["Find"] = ["Overview", "Summary", "Detail"],
            ["Read"] = ["Summary", "Overview", "Full"],
            ["Scan"] = ["Scan"],
        };

        foreach (var (label, variants) in expected)
        {
            var action = vm.CameraActions.Single(item => item.Label == label);

            await vm.ActivateCameraActionAsync(action);

            vm.ActiveCameraAction.Should().Be(action);
            vm.ShowCameraActionRail.Should().BeFalse();
            vm.ActiveCameraActionVariants.Select(variant => variant.Label)
                .Should()
                .Equal(variants);
            vm.Entries.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task SelectCameraActionFromUiAsync_SelectsRegisteredActionWithoutExecutingFallback()
    {
        var vm = CreateVm(
            [RecordingCommand.Read()],
            actions: [CameraAction(AssistiveActionIds.Read, "Read", "read")]);
        var fallbackExecuted = false;

        await vm.SelectCameraActionFromUiAsync(
            AssistiveActionIds.Read,
            () =>
            {
                fallbackExecuted = true;
                return Task.CompletedTask;
            });

        fallbackExecuted.Should().BeFalse();
        vm.ActiveCameraAction.Should().Be(vm.CameraActions.Single());
        vm.ShowCameraActionRail.Should().BeFalse();
        vm.ActiveCameraActionVariants.Select(variant => variant.Label)
            .Should()
            .Equal("Summary", "Overview", "Full");
        vm.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteCameraActionVariantAsync_AddsCapturedStillAndRunsCommandWithSameFrame()
    {
        byte[] frame = [0xFF, 0xD8, 0x01];
        var command = RecordingCommand.Look();
        var vm = CreateVm([command], frame);
        command.Finish.SetResult();

        await vm.ActivateCameraActionAsync(vm.CameraActions.Single());
        var variant = vm.ActiveCameraActionVariants.Single(item => item.Label == "Overview");

        await vm.ExecuteCameraActionVariantAsync(variant);

        command.CapturedFrame.Should().Equal(
            frame,
            string.Join(" | ", vm.Entries.Select(entry => $"{entry.Role}:{entry.Text}")));
        command.LastRequest?.Mode.Should().Be(CameraCommandMode.ManualAim);

        vm.Entries.Should().HaveCount(2);
        var input = vm.Entries[0];
        input.Role.Should().Be("You");
        input.Text.Should().Be("Look.");
        input.ImageCaption.Should().Be("Captured frame for Look - Overview");

        var output = vm.Entries[1];
        output.Role.Should().Be("AI");
        output.IsThinking.Should().BeFalse();
        output.Text.Should().Be("Look result.");
        AssertCameraActionSurfaceClosed(vm);
    }

    [Fact]
    public async Task ExecuteCameraActionVariantAsync_CapturesThroughCameraManagerProvider()
    {
        byte[] frame = [0xFF, 0xD8, 0x04];
        var command = RecordingCommand.Look();
        var provider = new FixedFrameCameraProvider(frame);
        var settings = new FakeSettingsService();
        var cameraManager = new CameraManager(
            [provider],
            settings,
            new DefaultCameraSelector(),
            NullLogger<CameraManager>.Instance,
            null);
        await cameraManager.InitializeAsync();
        command.Finish.SetResult();

        var vm = CreateVm(
            [command],
            settings: settings,
            cameraManager: cameraManager,
            useCameraActionFrameCapture: false);

        await vm.ActivateCameraActionAsync(vm.CameraActions.Single());
        var variant = vm.ActiveCameraActionVariants.Single(item => item.Label == "Overview");

        await vm.ExecuteCameraActionVariantAsync(variant);

        provider.CaptureCount.Should().Be(1);
        command.CapturedFrame.Should().Equal(frame);
        vm.Entries.Should().HaveCount(2);
        AssertCameraActionSurfaceClosed(vm);
    }

    [Fact]
    public async Task ExecuteCameraActionVariantAsync_ShowsBusyDotsUntilCommandCompletes()
    {
        byte[] frame = [0xFF, 0xD8, 0x02];
        var command = RecordingCommand.Look();
        var vm = CreateVm([command], frame);
        await vm.ActivateCameraActionAsync(vm.CameraActions.Single());
        var variant = vm.ActiveCameraActionVariants.Single(item => item.Label == "Detail");

        var task = vm.ExecuteCameraActionVariantAsync(variant);
        await command.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var aiEntry = vm.Entries.Single(entry => entry.Role == "AI");

        AssertCameraActionSurfaceClosed(vm);
        aiEntry.IsThinking.Should().BeTrue();
        await WaitUntilAsync(() => aiEntry.Text is "." or ".." or "...");

        command.Finish.SetResult();
        await task.WaitAsync(TimeSpan.FromSeconds(2));

        aiEntry.IsThinking.Should().BeFalse();
        aiEntry.Text.Should().Be("Look result.");
    }

    [Fact]
    public async Task ExecuteCameraActionVariantAsync_HidesActionRowsWhileFrameCaptureIsPending()
    {
        byte[] frame = [0xFF, 0xD8, 0x03];
        var captureStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishCapture = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = RecordingCommand.Look();
        MainViewModel? vm = null;
        var selectionClearedBeforeCapture = false;
        vm = CreateVm(
            [command],
            captureFrame: _ =>
            {
                var currentVm = vm!;
                selectionClearedBeforeCapture =
                    currentVm.ShowInlineCameraPreview
                    && currentVm.ShowCameraActionsSection
                    && !currentVm.ShowCameraActionRail
                    && currentVm.ActiveCameraAction is null
                    && currentVm.ActiveCameraActionVariants.Count == 0
                    && !currentVm.HasActiveCameraActionVariants;
                captureStarted.SetResult();
                return finishCapture.Task;
            });

        await vm.ActivateCameraActionAsync(vm.CameraActions.Single());
        var variant = vm.ActiveCameraActionVariants.Single(item => item.Label == "Overview");

        var task = vm.ExecuteCameraActionVariantAsync(variant);
        await captureStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        selectionClearedBeforeCapture.Should().BeTrue();
        vm.ShowInlineCameraPreview.Should().BeTrue();
        vm.ShowCameraActionsSection.Should().BeTrue();
        AssertCameraActionSelectionCleared(vm);
        vm.Entries.Should().BeEmpty();

        finishCapture.SetResult(frame);
        await command.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        command.Finish.SetResult();
        await task.WaitAsync(TimeSpan.FromSeconds(2));

        command.CapturedFrame.Should().Equal(frame);
        vm.Entries.Should().HaveCount(2);
        AssertCameraActionSurfaceClosed(vm);
    }

    [Fact]
    public async Task ExecuteCameraActionVariantAsync_ClosesSurfaceWhenFrameCaptureFails()
    {
        var vm = CreateVm([RecordingCommand.Look()], frame: null);

        await vm.ActivateCameraActionAsync(vm.CameraActions.Single());
        var variant = vm.ActiveCameraActionVariants.Single(item => item.Label == "Overview");

        await vm.ExecuteCameraActionVariantAsync(variant);

        vm.Entries.Should().ContainSingle();
        vm.Entries.Single().Text.Should().Be("Camera not available or no frame captured.");
        AssertCameraActionSurfaceClosed(vm);
    }

    [Fact]
    public async Task ExecuteCameraActionVariantAsync_DoesNotLeakCaptureExceptionToTranscript()
    {
        var vm = CreateVm(
            [RecordingCommand.Look()],
            captureFrame: _ => throw new InvalidOperationException("PlatformView cannot be null here"));

        await vm.ActivateCameraActionAsync(vm.CameraActions.Single());
        var variant = vm.ActiveCameraActionVariants.Single(item => item.Label == "Overview");

        await vm.ExecuteCameraActionVariantAsync(variant);

        vm.Entries.Should().ContainSingle();
        vm.Entries.Single().Role.Should().Be("AI");
        vm.Entries.Single().Text.Should().Be("Camera capture failed.");
        vm.Entries.Single().Text.Should().NotContain("PlatformView");
        vm.Entries.Single().Text.Should().NotContain("Command error");
        AssertCameraActionSurfaceClosed(vm);
    }

    private static void AssertCameraActionSurfaceClosed(MainViewModel vm)
    {
        vm.ShowInlineCameraPreview.Should().BeFalse();
        vm.ShowCameraActionsSection.Should().BeFalse();
        AssertCameraActionSelectionCleared(vm);
    }

    private static void AssertCameraActionSelectionCleared(MainViewModel vm)
    {
        vm.ShowCameraActionRail.Should().BeFalse();
        vm.ActiveCameraAction.Should().BeNull();
        vm.ActiveCameraActionVariants.Should().BeEmpty();
        vm.HasActiveCameraAction.Should().BeFalse();
        vm.HasActiveCameraActionVariants.Should().BeFalse();
        vm.CameraActions.Should().OnlyContain(action => !action.IsActive);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
        {
            await Task.Delay(20, cts.Token);
        }
    }

    private static MainViewModel CreateVm(
        IReadOnlyList<ICameraCommand> commands,
        byte[]? frame = null,
        IReadOnlyList<AssistiveActionDescriptor>? actions = null,
        Func<CancellationToken, Task<byte[]?>>? captureFrame = null,
        ISettingsService? settings = null,
        CameraManager? cameraManager = null,
        ILiveBarcodeScanner? liveBarcodeScanner = null,
        IProductBarcodeLookupWorkflow? productLookupWorkflow = null,
        bool useCameraActionFrameCapture = true)
    {
        var capturedFrame = frame;
        actions ??= commands
            .Select(command => CameraAction($"camera.{command.Id}", command.DisplayName, command.Id))
            .ToArray();
        settings ??= new FakeSettingsService();
        cameraManager ??= new CameraManager(
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
            cameraCommandRegistry: new FakeCameraCommandRegistry(commands),
            assistiveActionRegistry: new FakeAssistiveActionRegistry(actions),
            productLookupWorkflow: productLookupWorkflow,
            liveBarcodeScanner: liveBarcodeScanner,
            cameraActionFrameCapture: useCameraActionFrameCapture
                ? captureFrame ?? (_ => Task.FromResult<byte[]?>(capturedFrame))
                : null);
    }

    private static AssistiveActionDescriptor CameraAction(
        string id,
        string displayName,
        string commandId) =>
        new(
            id,
            displayName,
            RequiresCamera: true,
            StartsOrStopsSession: false,
            CameraCommandId: commandId);

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

    private sealed class FakeCameraCommandRegistry : ICameraCommandRegistry
    {
        public FakeCameraCommandRegistry(IReadOnlyList<ICameraCommand> commands)
        {
            Commands = commands;
        }

        public IReadOnlyList<ICameraCommand> Commands { get; }

        public bool TryGet(string id, out ICameraCommand command)
        {
            command = Commands.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase))!;
            return command is not null;
        }

        public bool TryGetTool(string toolName, out ICameraCommand command)
        {
            command = Commands.FirstOrDefault(item => string.Equals(item.ToolName, toolName, StringComparison.OrdinalIgnoreCase))!;
            return command is not null;
        }

        public ICameraCommand GetRequired(string id) =>
            TryGet(id, out var command)
                ? command
                : throw new InvalidOperationException($"Unknown camera command '{id}'.");
    }

    private sealed class FakeAssistiveActionRegistry : IAssistiveActionRegistry
    {
        public FakeAssistiveActionRegistry(IReadOnlyList<AssistiveActionDescriptor> actions)
        {
            Actions = actions;
        }

        public IReadOnlyList<AssistiveActionDescriptor> Actions { get; }

        public bool TryGet(string id, out IAssistiveAction action)
        {
            action = null!;
            return false;
        }

        public IAssistiveAction GetRequired(string id) =>
            throw new InvalidOperationException($"Unknown assistive action '{id}'.");
    }

    private sealed class RecordingCommand : ICameraCommand, ICameraActionVariantProvider
    {
        private RecordingCommand(
            string id,
            string displayName,
            IReadOnlyList<CameraActionVariantDefinition> variants)
        {
            Id = id;
            DisplayName = displayName;
            CameraActionVariants = variants;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public string? ToolName => Id;
        public CameraCommandCapabilities Capabilities { get; } = new(
            SupportsFullAuto: true,
            SupportsManualAim: true,
            RequiresStillFrame: true,
            CanUseFrameStream: false,
            RequiresConfirmationForExternalActions: false);
        public IReadOnlyList<CommandOptionDefinition> Options => [];
        public IReadOnlyList<CameraActionVariantDefinition> CameraActionVariants { get; }
        public CameraCommandRequest? LastRequest { get; private set; }
        public byte[]? CapturedFrame { get; private set; }
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Finish { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static RecordingCommand Look() =>
            new(
                "look",
                "Look",
                [
                    new("Overview", "Overview", "Look.", IsDefault: true),
                    new("Summary", "Summary", "Summary."),
                    new("Detailed", "Detail", "Detail.")
                ]);

        public static RecordingCommand Read() =>
            new(
                "read",
                "Read",
                [
                    new("Summary", "Summary", "Read summary."),
                    new("Overview", "Overview", "Read overview."),
                    new("Full", "Full", "Read full.", IsDefault: true)
                ]);

        public static RecordingCommand Single(string id, string displayName) =>
            new(id, displayName, [new("Default", displayName, displayName, IsDefault: true)]);

        public CameraCommandMode ResolveMode(
            CameraCommandRequest request,
            CameraCommandContext context) =>
            request.Mode ?? CameraCommandMode.FullAuto;

        public async Task<CameraCommandResult> ExecuteAsync(
            CameraCommandContext context,
            CancellationToken ct)
        {
            LastRequest = context.Request;
            CapturedFrame = context.ResolvedMode == CameraCommandMode.ManualAim
                ? await context.WaitForManualCapture(ct)
                : await context.CaptureFrame(ct);

            Started.TrySetResult();
            await Finish.Task.WaitAsync(ct);

            return new CameraCommandResult(
                Id,
                Success: true,
                TranscriptText: $"{DisplayName} result.",
                Data: new Dictionary<string, object?>
                {
                    ["mode"] = context.ResolvedMode.ToString()
                },
                Error: null);
        }
    }

    private sealed class FixedFrameCameraProvider(byte[]? frame) : ICameraProvider
    {
        public string DisplayName => "Fixed Frame Camera";
        public string ProviderId => "phone";
        public bool IsAvailable => true;
        public bool SupportsVideoRecording => false;
        public int CaptureCount { get; private set; }
        public int StartCount { get; private set; }
        public event EventHandler? Disconnected;

        public Task StartAsync(CancellationToken ct = default)
        {
            StartCount++;
            return Task.CompletedTask;
        }

        public Task StopAsync() => Task.CompletedTask;

        public Task<byte[]?> CaptureFrameAsync(CancellationToken ct = default)
        {
            CaptureCount++;
            return Task.FromResult<byte[]?>(frame);
        }

        public async IAsyncEnumerable<byte[]> StreamFramesAsync(
            [EnumeratorCancellation] CancellationToken ct)
        {
            if (frame is not null)
                yield return frame;

            await Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingLiveBarcodeScanner : ILiveBarcodeScanner
    {
        public TaskCompletionSource WatchStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int WatchCalls { get; private set; }

        public async IAsyncEnumerable<LiveBarcodeDetection> WatchAsync(
            IAsyncEnumerable<byte[]> frames,
            LiveBarcodeScannerOptions? options = null,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            WatchCalls++;

            await foreach (var _ in frames.WithCancellation(ct))
            {
                WatchStarted.TrySetResult();
                yield break;
            }
        }
    }

    private sealed class DetectionLiveBarcodeScanner : ILiveBarcodeScanner
    {
        private readonly LiveBarcodeDetection _detection;

        public DetectionLiveBarcodeScanner(LiveBarcodeDetection detection)
        {
            _detection = detection;
        }

        public async IAsyncEnumerable<LiveBarcodeDetection> WatchAsync(
            IAsyncEnumerable<byte[]> frames,
            LiveBarcodeScannerOptions? options = null,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await foreach (var _ in frames.WithCancellation(ct))
            {
                yield return _detection;
                yield break;
            }
        }
    }

    private sealed class ReopenLiveBarcodeScanner : ILiveBarcodeScanner
    {
        private readonly LiveBarcodeDetection _firstDetection;

        public ReopenLiveBarcodeScanner(LiveBarcodeDetection firstDetection)
        {
            _firstDetection = firstDetection;
        }

        public int WatchCalls { get; private set; }
        public TaskCompletionSource SecondWatchStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async IAsyncEnumerable<LiveBarcodeDetection> WatchAsync(
            IAsyncEnumerable<byte[]> frames,
            LiveBarcodeScannerOptions? options = null,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            WatchCalls++;
            await foreach (var _ in frames.WithCancellation(ct))
            {
                if (WatchCalls == 1)
                {
                    yield return _firstDetection;
                    yield break;
                }

                SecondWatchStarted.TrySetResult();
                yield break;
            }
        }
    }

    private sealed class FixedProductLookupWorkflow : IProductBarcodeLookupWorkflow
    {
        private readonly ProductBarcodeLookupResult _result;

        public FixedProductLookupWorkflow(ProductBarcodeLookupResult result)
        {
            _result = result;
        }

        public int Calls { get; private set; }

        public Task<ProductBarcodeLookupResult> LookupAsync(
            Func<CancellationToken, Task<byte[]?>> captureFrame,
            string? barcode = null,
            CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(_result);
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
