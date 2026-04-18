# M15 Phase 2 — Test DI Infrastructure

**Goal:** Build the plumbing that swaps production providers for test providers so
BodyCam launches in test mode with controllable hardware.

**Depends on:** Phase 1 (test providers exist and compile).

---

## What We're Building

| Artifact | Purpose |
|----------|---------|
| `TestMauiProgram` | Alternate `CreateMauiApp` that registers test providers |
| `TestServiceAccessor` | Static accessor to retrieve test providers for assertions |
| `BodyCamTestFixture` | xUnit fixture base that manages app lifecycle |
| Environment variable config | `BODYCAM_TEST_MODE`, `BODYCAM_TEST_ASSETS` |

---

## Wave 1: TestMauiProgram — Test-Mode DI Registration

The production `MauiProgram.CreateMauiApp()` registers platform providers behind
`#if WINDOWS` / `#if ANDROID` guards. In test mode, we skip platform providers
entirely and register test providers instead.

### Strategy: Environment Variable Switch

Add a test-mode branch to `MauiProgram.cs` itself. This keeps one entry point
(important for MAUI — the app host must be the same binary).

```csharp
// In MauiProgram.CreateMauiApp(), before platform provider registration:

var testMode = Environment.GetEnvironmentVariable("BODYCAM_TEST_MODE") == "1";

if (testMode)
{
    RegisterTestProviders(builder);
}
else
{
    // Existing platform provider registration
    #if WINDOWS
    builder.Services.AddSingleton<IAudioInputProvider, PlatformMicProvider>();
    // ...
    #endif
}
```

### RegisterTestProviders Method

```csharp
private static void RegisterTestProviders(MauiAppBuilder builder)
{
    var assetsPath = Environment.GetEnvironmentVariable("BODYCAM_TEST_ASSETS")
        ?? Path.Combine(AppContext.BaseDirectory, "TestAssets");

    // Audio input: silence by default (tests can replace via DI)
    var silencePath = Path.Combine(assetsPath, "Audio", "silence-1s.pcm");
    if (File.Exists(silencePath))
        builder.Services.AddSingleton<IAudioInputProvider>(new TestMicProvider(silencePath));
    else
        builder.Services.AddSingleton<IAudioInputProvider>(new TestMicProvider(new byte[3200]));

    // Audio output: capture for assertions
    builder.Services.AddSingleton<TestSpeakerProvider>();
    builder.Services.AddSingleton<IAudioOutputProvider>(sp =>
        sp.GetRequiredService<TestSpeakerProvider>());

    // Camera: test frames
    var framesPath = Path.Combine(assetsPath, "Frames");
    if (Directory.Exists(framesPath) && Directory.GetFiles(framesPath, "*.jpg").Length > 0)
        builder.Services.AddSingleton<ICameraProvider>(new TestCameraProvider(framesPath));
    else
        builder.Services.AddSingleton<ICameraProvider>(new TestCameraProvider(MinimalJpeg()));

    // Button input: programmable
    builder.Services.AddSingleton<TestButtonProvider>();
    builder.Services.AddSingleton<IButtonInputProvider>(sp =>
        sp.GetRequiredService<TestButtonProvider>());

    // Store service provider for test access
    builder.Services.AddSingleton<TestServiceAccessor>();
}

private static byte[] MinimalJpeg()
{
    // 1x1 white JPEG — valid enough for pipeline tests
    return Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRof" +
        "Hh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/2wBDAQkJCQwLDBgNDRgyIRwh" +
        "MjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjL/wAAR" +
        "CAABAAEDASIAAhEBAxEB/8QAFAABAAAAAAAAAAAAAAAAAAAACf/EABQQAQAAAAAAAAAAAAAAAAAA" +
        "AAD/xAAUAQEAAAAAAAAAAAAAAAAAAAAA/8QAFBEBAAAAAAAAAAAAAAAAAAAAAP/aAAwDAQACEQMR" +
        "AD8AKwA//9k=");
}
```

---

## Wave 2: TestServiceAccessor — Typed Provider Access

Tests need to reach into the running app's DI container to get test providers
for assertions (e.g., "was audio played?"). A static accessor avoids coupling
tests to MAUI internals.

```csharp
namespace BodyCam.Tests.TestInfrastructure;

/// <summary>
/// Provides typed access to test providers registered in the app's DI container.
/// Registered as a singleton in test mode — tests resolve it to make assertions.
/// </summary>
public class TestServiceAccessor
{
    private readonly IServiceProvider _services;

    public TestServiceAccessor(IServiceProvider services)
    {
        _services = services;
    }

    public TestSpeakerProvider Speaker =>
        _services.GetRequiredService<TestSpeakerProvider>();

    public TestButtonProvider Buttons =>
        _services.GetRequiredService<TestButtonProvider>();

    // Camera and mic are registered as interface — resolve concrete type
    public TestCameraProvider Camera =>
        (TestCameraProvider)_services.GetRequiredService<ICameraProvider>();

    public TestMicProvider Mic =>
        (TestMicProvider)_services.GetRequiredService<IAudioInputProvider>();

    /// <summary>Reset all providers between tests.</summary>
    public void ResetAll()
    {
        Speaker.Reset();
        Buttons.Reset();
        Camera.Reset();
    }
}
```

### Static Access Pattern

For UI tests where the test process and app process are the same binary
(in-process MAUI testing):

```csharp
/// <summary>
/// Global accessor set during app startup in test mode.
/// </summary>
public static class TestServices
{
    public static TestServiceAccessor? Current { get; internal set; }
}
```

Set it in `App.xaml.cs` or after `MauiApp.Build()`:

```csharp
// In MauiProgram, after Build():
if (testMode)
{
    var app = builder.Build();
    TestServices.Current = app.Services.GetService<TestServiceAccessor>();
    return app;
}
```

---

## Wave 3: BodyCamTestFixture — xUnit Fixture for Integration Tests

For integration tests that don't need a full UI (test providers + real managers +
real tool dispatcher), create a fixture that builds the service graph without MAUI.

```csharp
using Microsoft.Extensions.DependencyInjection;
using BodyCam.Tests.TestInfrastructure.Providers;
using BodyCam.Services.Audio;
using BodyCam.Services.Camera;
using BodyCam.Services.Input;
using BodyCam.Orchestration;
using BodyCam.Tools;

namespace BodyCam.Tests.TestInfrastructure;

/// <summary>
/// Builds a BodyCam service graph with test providers for integration tests.
/// No MAUI host, no UI — just services, managers, and tools.
/// </summary>
public class BodyCamTestFixture : IDisposable
{
    public IServiceProvider Services { get; }
    public TestServiceAccessor TestProviders { get; }

    public BodyCamTestFixture()
    {
        var services = new ServiceCollection();

        // Test providers
        var mic = new TestMicProvider(new byte[3200]);
        var speaker = new TestSpeakerProvider();
        var camera = new TestCameraProvider(MinimalJpeg());
        var buttons = new TestButtonProvider();

        services.AddSingleton<IAudioInputProvider>(mic);
        services.AddSingleton<IAudioOutputProvider>(speaker);
        services.AddSingleton<TestSpeakerProvider>(speaker);
        services.AddSingleton<ICameraProvider>(camera);
        services.AddSingleton<IButtonInputProvider>(buttons);
        services.AddSingleton<TestButtonProvider>(buttons);

        // Real managers (they don't care about provider type)
        services.AddSingleton<AudioInputManager>();
        services.AddSingleton<IAudioInputService>(sp =>
            sp.GetRequiredService<AudioInputManager>());
        services.AddSingleton<AudioOutputManager>();
        services.AddSingleton<IAudioOutputService>(sp =>
            sp.GetRequiredService<AudioOutputManager>());
        services.AddSingleton<CameraManager>();
        services.AddSingleton<ButtonInputManager>();

        // Tools and dispatcher
        RegisterAllTools(services);
        services.AddSingleton<ToolDispatcher>();

        // Test accessor
        services.AddSingleton<TestServiceAccessor>();

        Services = services.BuildServiceProvider();
        TestProviders = Services.GetRequiredService<TestServiceAccessor>();
    }

    private void RegisterAllTools(ServiceCollection services)
    {
        // Register all ITool implementations
        // (same list as MauiProgram.cs)
        services.AddSingleton<ITool, DescribeSceneTool>();
        services.AddSingleton<ITool, ReadTextTool>();
        services.AddSingleton<ITool, TakePhotoTool>();
        services.AddSingleton<ITool, SaveMemoryTool>();
        services.AddSingleton<ITool, RecallMemoryTool>();
        services.AddSingleton<ITool, FindObjectTool>();
        services.AddSingleton<ITool, NavigateToTool>();
        services.AddSingleton<ITool, StartSceneWatchTool>();
        services.AddSingleton<ITool, MakePhoneCallTool>();
        services.AddSingleton<ITool, SendMessageTool>();
        services.AddSingleton<ITool, LookupAddressTool>();
        services.AddSingleton<ITool, DeepAnalysisTool>();
        services.AddSingleton<ITool, SetTranslationModeTool>();
    }

    private static byte[] MinimalJpeg() =>
        Convert.FromBase64String(
            "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8U" +
            "HRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/2wBDAQkJCQwLDBgN" +
            "DRgyIRwhMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIy" +
            "MjIyMjL/wAARCAABAAEDASIAAhEBAxEB/8QAFAABAAAAAAAAAAAAAAAAAAAACf/EABQQAQAA" +
            "AAAAAAAAAAAAAAAAAD/xAAUAQEAAAAAAAAAAAAAAAAAAAAA/8QAFBEBAAAAAAAAAAAAAAAAA" +
            "AAAAP/aAAwDAQACEQMRAD8AKwA//9k=");

    public void Dispose()
    {
        if (Services is IDisposable d) d.Dispose();
    }
}
```

### Usage in Integration Tests

```csharp
public class ToolDispatcherIntegrationTests : IClassFixture<BodyCamTestFixture>
{
    private readonly BodyCamTestFixture _fixture;
    private readonly ToolDispatcher _dispatcher;

    public ToolDispatcherIntegrationTests(BodyCamTestFixture fixture)
    {
        _fixture = fixture;
        _dispatcher = fixture.Services.GetRequiredService<ToolDispatcher>();
        _fixture.TestProviders.ResetAll();
    }

    [Fact]
    public async Task DescribeScene_WithTestCamera_CapturesFrame()
    {
        var context = CreateToolContext();
        var result = await _dispatcher.ExecuteAsync("describe_scene", null, context, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _fixture.TestProviders.Camera.FramesCaptured.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task TakePhoto_SavesFile()
    {
        var context = CreateToolContext();
        var result = await _dispatcher.ExecuteAsync("take_photo", null, context, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }
}
```

---

## Wave 4: Environment Variable Configuration

### Variables

| Variable | Default | Purpose |
|----------|---------|---------|
| `BODYCAM_TEST_MODE` | unset | Set to `"1"` to enable test providers |
| `BODYCAM_TEST_ASSETS` | `{BaseDirectory}/TestAssets` | Path to test asset files |
| `BODYCAM_TEST_API` | unset | Set to `"real"` to use live OpenAI API in test mode |

### Launch Profile

Add to `src/BodyCam/Properties/launchSettings.json`:

```json
{
  "profiles": {
    "BodyCam (Test Mode)": {
      "commandName": "Project",
      "environmentVariables": {
        "BODYCAM_TEST_MODE": "1",
        "BODYCAM_TEST_ASSETS": "$(SolutionDir)src/BodyCam.Tests/TestInfrastructure/TestAssets"
      }
    }
  }
}
```

### UI Test Configuration

Set env vars before launching the app in `BodyCamFixture`:

```csharp
protected override MauiTestContextOptions CreateTestContextOptions()
{
    // Set environment before app launches
    Environment.SetEnvironmentVariable("BODYCAM_TEST_MODE", "1");
    Environment.SetEnvironmentVariable("BODYCAM_TEST_ASSETS",
        Path.Combine(TestContext.CurrentContext.TestDirectory, "TestAssets"));

    return base.CreateTestContextOptions();
}
```

---

## Verification

After all waves:

1. App launches in test mode with `BODYCAM_TEST_MODE=1` — no hardware errors
2. `TestServiceAccessor` resolves all four test providers
3. `BodyCamTestFixture` builds service graph with tools and managers
4. Integration tests can dispatch tools and assert on provider state
5. `ResetAll()` clears provider state cleanly between tests
