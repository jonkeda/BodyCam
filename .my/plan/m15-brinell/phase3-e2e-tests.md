# M15 Phase 3 — End-to-End Test Scenarios

**Goal:** Write tests that exercise the full pipeline — trigger → manager → tool →
result → output — using test providers from Phase 1 and DI infrastructure from Phase 2.

**Depends on:** Phase 1 (test providers), Phase 2 (DI infrastructure).

---

## What We're Building

Three tiers of end-to-end tests, each covering progressively more of the stack:

| Tier | Test Project | What It Tests | API |
|------|-------------|---------------|-----|
| **Integration** | `BodyCam.Tests` | Tool execution through managers with test providers | Mocked |
| **Integration (Real API)** | `BodyCam.RealTests` | Same pipeline but with live OpenAI | Live |
| **UI** | `BodyCam.UITests` | Full app running, Brinell drives UI + asserts provider state | Either |

---

## Wave 1: Integration Tests — Tool Execution via Managers

These tests use `BodyCamTestFixture` (Phase 2). No MAUI, no UI. They verify that
tools receive frames from `CameraManager`, audio flows through managers, and
buttons dispatch correctly through `ButtonInputManager`.

### Test File: `ToolPipelineTests.cs`

```csharp
using BodyCam.Tests.TestInfrastructure;
using BodyCam.Tests.TestInfrastructure.Providers;
using BodyCam.Services.Camera;
using BodyCam.Services.Input;
using BodyCam.Tools;

namespace BodyCam.Tests.Integration;

public class ToolPipelineTests : IClassFixture<BodyCamTestFixture>
{
    private readonly BodyCamTestFixture _fixture;
    private readonly ToolDispatcher _dispatcher;

    public ToolPipelineTests(BodyCamTestFixture fixture)
    {
        _fixture = fixture;
        _dispatcher = fixture.Services.GetRequiredService<ToolDispatcher>();
        _fixture.TestProviders.ResetAll();
    }

    // --- describe_scene ---

    [Fact]
    public async Task DescribeScene_CapturesFrameFromTestCamera()
    {
        var context = _fixture.CreateToolContext();

        var result = await _dispatcher.ExecuteAsync(
            "describe_scene", null, context, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _fixture.TestProviders.Camera.FramesCaptured.Should().Be(1);
    }

    [Fact]
    public async Task DescribeScene_WithQuery_PassesQueryToVision()
    {
        var context = _fixture.CreateToolContext();
        var args = """{"query": "what color is the wall?"}""";

        var result = await _dispatcher.ExecuteAsync(
            "describe_scene", args, context, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task DescribeScene_CameraUnavailable_ReturnsError()
    {
        _fixture.TestProviders.Camera.IsAvailable = false;
        var context = _fixture.CreateToolContext();

        var result = await _dispatcher.ExecuteAsync(
            "describe_scene", null, context, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    // --- read_text ---

    [Fact]
    public async Task ReadText_CapturesFrameAndReturnsResult()
    {
        var context = _fixture.CreateToolContext();

        var result = await _dispatcher.ExecuteAsync(
            "read_text", null, context, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _fixture.TestProviders.Camera.FramesCaptured.Should().Be(1);
    }

    [Fact]
    public async Task ReadText_WithFocus_PassesFocusParameter()
    {
        var context = _fixture.CreateToolContext();
        var args = """{"focus": "menu"}""";

        var result = await _dispatcher.ExecuteAsync(
            "read_text", args, context, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    // --- take_photo ---

    [Fact]
    public async Task TakePhoto_CapturesAndSavesFile()
    {
        var context = _fixture.CreateToolContext();

        var result = await _dispatcher.ExecuteAsync(
            "take_photo", null, context, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _fixture.TestProviders.Camera.FramesCaptured.Should().Be(1);
        // Verify file exists in AppData/photos/
    }

    // --- save_memory + recall_memory ---

    [Fact]
    public async Task SaveAndRecall_RoundTrip()
    {
        var context = _fixture.CreateToolContext();

        // Save
        var saveArgs = """{"content": "Car is in spot B7", "category": "location"}""";
        var saveResult = await _dispatcher.ExecuteAsync(
            "save_memory", saveArgs, context, CancellationToken.None);
        saveResult.IsSuccess.Should().BeTrue();

        // Recall
        var recallArgs = """{"query": "car"}""";
        var recallResult = await _dispatcher.ExecuteAsync(
            "recall_memory", recallArgs, context, CancellationToken.None);
        recallResult.IsSuccess.Should().BeTrue();
        recallResult.Json.Should().Contain("B7");
    }

    [Fact]
    public async Task RecallMemory_NoMatch_ReturnsEmpty()
    {
        var context = _fixture.CreateToolContext();
        var args = """{"query": "nonexistent thing"}""";

        var result = await _dispatcher.ExecuteAsync(
            "recall_memory", args, context, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // Result indicates no memories found
    }

    // --- find_object ---

    [Fact]
    public async Task FindObject_PollsCameraMultipleTimes()
    {
        var context = _fixture.CreateToolContext();
        var args = """{"target": "red mug"}""";

        var result = await _dispatcher.ExecuteAsync(
            "find_object", args, context, CancellationToken.None);

        // find_object polls every 3s — camera should have multiple captures
        _fixture.TestProviders.Camera.FramesCaptured.Should().BeGreaterThan(1);
    }

    // --- navigate_to ---

    [Fact]
    public async Task NavigateTo_ReturnsSuccessWithUri()
    {
        var context = _fixture.CreateToolContext();
        var args = """{"destination": "Central Park", "mode": "walking"}""";

        var result = await _dispatcher.ExecuteAsync(
            "navigate_to", args, context, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    // --- set_translation_mode ---

    [Fact]
    public async Task SetTranslationMode_EnableAndDisable()
    {
        var context = _fixture.CreateToolContext();

        // Enable
        var enableArgs = """{"targetLanguage": "Spanish", "active": true}""";
        var enableResult = await _dispatcher.ExecuteAsync(
            "set_translation_mode", enableArgs, context, CancellationToken.None);
        enableResult.IsSuccess.Should().BeTrue();

        // Disable
        var disableArgs = """{"targetLanguage": "Spanish", "active": false}""";
        var disableResult = await _dispatcher.ExecuteAsync(
            "set_translation_mode", disableArgs, context, CancellationToken.None);
        disableResult.IsSuccess.Should().BeTrue();
    }
}
```

---

## Wave 2: Button Dispatch Tests

Test the full button → gesture → action → tool chain using `TestButtonProvider`
and `ButtonInputManager`.

### Test File: `ButtonDispatchTests.cs`

```csharp
using BodyCam.Tests.TestInfrastructure;
using BodyCam.Services.Input;

namespace BodyCam.Tests.Integration;

public class ButtonDispatchTests : IClassFixture<BodyCamTestFixture>
{
    private readonly BodyCamTestFixture _fixture;
    private readonly ButtonInputManager _buttonManager;

    public ButtonDispatchTests(BodyCamTestFixture fixture)
    {
        _fixture = fixture;
        _buttonManager = fixture.Services.GetRequiredService<ButtonInputManager>();
        _fixture.TestProviders.ResetAll();
    }

    [Fact]
    public async Task SingleTap_TriggersLookAction()
    {
        ButtonActionEvent? received = null;
        _buttonManager.ActionTriggered += (_, e) => received = e;

        await _buttonManager.StartAsync();
        _fixture.TestProviders.Buttons.SimulateGesture(ButtonGesture.SingleTap);

        // Default mapping: SingleTap → Look
        received.Should().NotBeNull();
        received!.Action.Should().Be(ButtonAction.Look);
    }

    [Fact]
    public async Task DoubleTap_TriggersPhotoAction()
    {
        ButtonActionEvent? received = null;
        _buttonManager.ActionTriggered += (_, e) => received = e;

        await _buttonManager.StartAsync();
        _fixture.TestProviders.Buttons.SimulateGesture(ButtonGesture.DoubleTap);

        received.Should().NotBeNull();
        received!.Action.Should().Be(ButtonAction.Photo);
    }

    [Fact]
    public async Task LongPress_TriggersToggleSession()
    {
        ButtonActionEvent? received = null;
        _buttonManager.ActionTriggered += (_, e) => received = e;

        await _buttonManager.StartAsync();
        _fixture.TestProviders.Buttons.SimulateGesture(ButtonGesture.LongPress);

        received.Should().NotBeNull();
        received!.Action.Should().Be(ButtonAction.ToggleSession);
    }

    [Fact]
    public async Task CustomMapping_OverridesDefault()
    {
        ButtonActionEvent? received = null;
        _buttonManager.ActionTriggered += (_, e) => received = e;
        _buttonManager.ActionMap.SetAction("test-buttons:main", ButtonGesture.SingleTap, ButtonAction.Photo);

        await _buttonManager.StartAsync();
        _fixture.TestProviders.Buttons.SimulateGesture(ButtonGesture.SingleTap);

        received!.Action.Should().Be(ButtonAction.Photo);
    }

    [Fact]
    public async Task RawClick_GoesThrough_GestureRecognizer()
    {
        ButtonActionEvent? received = null;
        _buttonManager.ActionTriggered += (_, e) => received = e;

        await _buttonManager.StartAsync();
        _fixture.TestProviders.Buttons.SimulateClick("main");

        // Wait for gesture recognizer debounce
        await Task.Delay(500);

        received.Should().NotBeNull();
        // Raw click → GestureRecognizer detects SingleTap → Look
        received!.Action.Should().Be(ButtonAction.Look);
    }
}
```

---

## Wave 3: Provider Fallback & Disconnect Tests

Test that managers handle provider disconnects gracefully.

### Test File: `ProviderFallbackTests.cs`

```csharp
namespace BodyCam.Tests.Integration;

public class ProviderFallbackTests : IClassFixture<BodyCamTestFixture>
{
    private readonly BodyCamTestFixture _fixture;

    public ProviderFallbackTests(BodyCamTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.TestProviders.ResetAll();
    }

    [Fact]
    public async Task CameraDisconnect_ManagerHandlesGracefully()
    {
        var cameraManager = _fixture.Services.GetRequiredService<CameraManager>();
        await cameraManager.InitializeAsync();

        // First capture works
        var frame = await cameraManager.Active!.CaptureFrameAsync();
        frame.Should().NotBeNull();

        // Disconnect
        _fixture.TestProviders.Camera.SimulateDisconnect();

        // Next capture should return null (camera unavailable)
        var frame2 = await _fixture.TestProviders.Camera.CaptureFrameAsync();
        frame2.Should().BeNull();
    }

    [Fact]
    public async Task MicDisconnect_StopsCapturing()
    {
        var mic = _fixture.TestProviders.Mic;
        await mic.StartAsync();
        mic.IsCapturing.Should().BeTrue();

        mic.SimulateDisconnect();

        mic.IsCapturing.Should().BeFalse();
    }

    [Fact]
    public async Task SpeakerDisconnect_StopsPlaying()
    {
        var speaker = _fixture.TestProviders.Speaker;
        await speaker.StartAsync(24000);
        speaker.IsPlaying.Should().BeTrue();

        speaker.SimulateDisconnect();

        speaker.IsPlaying.Should().BeFalse();
    }

    [Fact]
    public async Task ButtonDisconnect_ManagerContinuesWithOtherProviders()
    {
        var buttonManager = _fixture.Services.GetRequiredService<ButtonInputManager>();
        await buttonManager.StartAsync();

        _fixture.TestProviders.Buttons.SimulateDisconnect();

        // Manager should not throw — it just has one fewer provider
        _fixture.TestProviders.Buttons.IsActive.Should().BeFalse();
    }
}
```

---

## Wave 4: Audio Flow Tests

Test the full audio round-trip: mic → manager → (pipeline) → manager → speaker.

### Test File: `AudioFlowTests.cs`

```csharp
namespace BodyCam.Tests.Integration;

public class AudioFlowTests : IClassFixture<BodyCamTestFixture>
{
    private readonly BodyCamTestFixture _fixture;

    public AudioFlowTests(BodyCamTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.TestProviders.ResetAll();
    }

    [Fact]
    public async Task MicChunks_FlowThroughAudioInputManager()
    {
        var chunks = new List<byte[]>();
        var manager = _fixture.Services.GetRequiredService<AudioInputManager>();

        // Subscribe to manager's forwarded events
        manager.AudioChunkAvailable += (_, chunk) => chunks.Add(chunk);
        await manager.InitializeAsync();

        // Wait for mic to emit some chunks
        await Task.Delay(500);

        chunks.Should().NotBeEmpty("mic should have emitted chunks through manager");
    }

    [Fact]
    public async Task SpeakerChunks_CapturedByTestProvider()
    {
        var manager = _fixture.Services.GetRequiredService<AudioOutputManager>();
        await manager.InitializeAsync();

        // Simulate the pipeline sending audio to the speaker
        var testPcm = new byte[] { 1, 2, 3, 4, 5 };
        await manager.Active!.PlayChunkAsync(testPcm);

        _fixture.TestProviders.Speaker.WasAudioPlayed.Should().BeTrue();
        _fixture.TestProviders.Speaker.TotalBytesPlayed.Should().Be(5);
    }

    [Fact]
    public async Task ClearBuffer_ResetsPlaybackState()
    {
        var speaker = _fixture.TestProviders.Speaker;
        await speaker.StartAsync(24000);
        await speaker.PlayChunkAsync(new byte[100]);

        speaker.ClearBuffer();

        speaker.ChunkCount.Should().Be(0);
        speaker.TotalBytesPlayed.Should().Be(0);
    }
}
```

---

## Wave 5: Real API Integration Tests (Optional — Requires Keys)

Extend the existing `BodyCam.RealTests` with test provider versions. These connect
to the live OpenAI Realtime API but use test camera frames instead of a real webcam.

### Test File: `TestProviderRealApiTests.cs`

```csharp
namespace BodyCam.RealTests;

/// <summary>
/// Real API tests using test providers instead of real hardware.
/// Requires OPENAI_API_KEY in .env.
/// </summary>
[Trait("Category", "RealAPI")]
public class TestProviderRealApiTests
{
    [SkippableFact]
    public async Task DescribeScene_WithTestFrame_ModelResponds()
    {
        Skip.If(string.IsNullOrEmpty(GetApiKey()), "No API key");

        // Build pipeline with test camera but real API
        var camera = new TestCameraProvider(LoadTestFrame("office-desk.jpg"));
        var client = CreateRealtimeClient();

        // Send frame to vision
        var frame = await camera.CaptureFrameAsync();
        var result = await client.SendVisionAsync(frame!);

        result.Should().NotBeNullOrEmpty();
        result.Should().ContainAny("desk", "monitor", "keyboard", "office");
    }

    [SkippableFact]
    public async Task ReadText_WithTestFrame_ExtractsText()
    {
        Skip.If(string.IsNullOrEmpty(GetApiKey()), "No API key");

        var camera = new TestCameraProvider(LoadTestFrame("text-sign.jpg"));
        var frame = await camera.CaptureFrameAsync();

        // Vision API should read text from the sign image
        var result = await SendToVision(frame!, "Read any text visible");
        result.Should().NotBeNullOrEmpty();
    }

    [SkippableFact]
    public async Task FullRoundTrip_Button_To_Audio()
    {
        Skip.If(string.IsNullOrEmpty(GetApiKey()), "No API key");

        // Build full pipeline: test camera + test speaker + real API
        var camera = new TestCameraProvider(LoadTestFrame("office-desk.jpg"));
        var speaker = new TestSpeakerProvider();
        var buttons = new TestButtonProvider();

        // Simulate: button press → describe_scene → API → audio response
        buttons.SimulateGesture(ButtonGesture.SingleTap);

        // Wait for round-trip
        await WaitForCondition(() => speaker.WasAudioPlayed, timeout: 30_000);

        speaker.WasAudioPlayed.Should().BeTrue(
            "AI should have responded with audio after seeing the test frame");
        speaker.TotalBytesPlayed.Should().BeGreaterThan(0);
    }
}
```

---

## Wave 6: UI Tests — Brinell-Driven E2E

These tests launch the real BodyCam app in test mode and drive it through
Brinell (FlaUI on Windows). They click buttons and verify that test providers
received/emitted the expected data.

### Updated BodyCamFixture

```csharp
public class BodyCamFixture : MauiTestFixtureBase
{
    public BodyCamFixture()
    {
        // Ensure test mode is active
        Environment.SetEnvironmentVariable("BODYCAM_TEST_MODE", "1");
    }

    protected override string GetDefaultAppPath(string platform)
        => platform == "windows"
            ? @"E:\repos\Private\BodyCam\src\BodyCam\bin\Debug\net10.0-windows10.0.19041.0\win-x64\BodyCam.exe"
            : throw new NotSupportedException();

    // Access test providers in the running app
    public TestServiceAccessor TestProviders
        => TestServices.Current ?? throw new InvalidOperationException(
            "App not running in test mode. Set BODYCAM_TEST_MODE=1");
}
```

### Test File: `LookPipelineUITests.cs`

```csharp
[Collection("BodyCam")]
[Trait("Category", "UITest")]
public class LookPipelineUITests
{
    private readonly BodyCamFixture _fixture;
    private MainPage Page => _fixture.MainPage;

    public LookPipelineUITests(BodyCamFixture fixture)
    {
        _fixture = fixture;
        _fixture.NavigateToHome();
        _fixture.TestProviders.ResetAll();
    }

    [Fact]
    public void LookButton_CapturesFrame()
    {
        // Start session first
        Page.SleepButton.Click();
        Thread.Sleep(2000); // wait for session to activate

        // Click Look
        Page.LookButton.Click();
        Thread.Sleep(3000); // wait for round-trip

        // Verify camera was used
        _fixture.TestProviders.Camera.FramesCaptured.Should().BeGreaterThan(0);
    }

    [Fact]
    public void LookButton_ProducesAudioResponse()
    {
        Page.SleepButton.Click();
        Thread.Sleep(2000);

        Page.LookButton.Click();
        Thread.Sleep(5000);

        _fixture.TestProviders.Speaker.WasAudioPlayed.Should().BeTrue();
    }

    [Fact]
    public void PhotoButton_CapturesAndSaves()
    {
        Page.PhotoButton.Click();
        Thread.Sleep(2000);

        _fixture.TestProviders.Camera.FramesCaptured.Should().BeGreaterThan(0);
    }
}
```

### Test File: `ButtonGestureUITests.cs`

```csharp
[Collection("BodyCam")]
[Trait("Category", "UITest")]
public class ButtonGestureUITests
{
    private readonly BodyCamFixture _fixture;

    public ButtonGestureUITests(BodyCamFixture fixture)
    {
        _fixture = fixture;
        _fixture.NavigateToHome();
        _fixture.TestProviders.ResetAll();
    }

    [Fact]
    public void SimulatedSingleTap_TriggersLook()
    {
        // Activate session
        _fixture.TestProviders.Buttons.SimulateGesture(ButtonGesture.LongPress);
        Thread.Sleep(2000);

        // Simulate Look gesture
        _fixture.TestProviders.Buttons.SimulateGesture(ButtonGesture.SingleTap);
        Thread.Sleep(3000);

        _fixture.TestProviders.Camera.FramesCaptured.Should().BeGreaterThan(0);
    }

    [Fact]
    public void SimulatedDoubleTap_TriggersPhoto()
    {
        _fixture.TestProviders.Buttons.SimulateGesture(ButtonGesture.DoubleTap);
        Thread.Sleep(2000);

        _fixture.TestProviders.Camera.FramesCaptured.Should().BeGreaterThan(0);
    }

    [Fact]
    public void SimulatedLongPress_TogglesSession()
    {
        var page = _fixture.MainPage;

        // Should start in Sleep
        _fixture.TestProviders.Buttons.SimulateGesture(ButtonGesture.LongPress);
        Thread.Sleep(2000);

        // Status should change to Active
        page.StatusLabel.GetText().Should().Contain("Active");

        // Long press again → Sleep
        _fixture.TestProviders.Buttons.SimulateGesture(ButtonGesture.LongPress);
        Thread.Sleep(2000);

        page.StatusLabel.GetText().Should().Contain("Sleep");
    }
}
```

---

## Test Coverage Matrix

Maps test cases from [test-cases.md](test-cases.md) to implementation waves:

| Test Case IDs | Wave | File |
|--------------|------|------|
| DS-BTN-3, DS-LLM-3, RT-LLM-1, RT-LLM-2, TP-LLM-1, SM-LLM-1, RM-LLM-1, RM-LLM-3 | Wave 1 | `ToolPipelineTests.cs` |
| SC-BTN-1..5, DS-BTN-1 (gesture) | Wave 2 | `ButtonDispatchTests.cs` |
| INT-4, INT-5 | Wave 3 | `ProviderFallbackTests.cs` |
| INT-9 (audio flow) | Wave 4 | `AudioFlowTests.cs` |
| DS-LLM-1, RT-LLM-1, INT-9 (real API) | Wave 5 | `TestProviderRealApiTests.cs` |
| DS-BTN-2, RT-BTN-1, TP-BTN-2, FO-BTN-1 | Wave 6 | `LookPipelineUITests.cs` |
| SC-BTN-1..3, INT-7 | Wave 6 | `ButtonGestureUITests.cs` |

---

## Verification

After all waves:

1. Integration tests pass: `dotnet test src/BodyCam.Tests` — all new tests green
2. Real API tests pass (with key): `dotnet test src/BodyCam.RealTests`
3. UI tests pass: app launches in test mode, Brinell drives buttons, assertions on providers pass
4. No test touches real hardware — all provider I/O goes through test implementations
