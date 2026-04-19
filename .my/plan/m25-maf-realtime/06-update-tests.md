# Step 06 — Update Tests

Update all test files that reference the old `IRealtimeClient` or `ToolContext.RealtimeClient`. This is mostly mechanical — remove one property from ToolContext, update constructor calls, and replace event-based test patterns with session mocks.

**Depends on:** Steps 02, 03, 04, 05  
**Touches:** 20+ test files  
**Tests affected:** All — this step makes them compile and pass

---

## What to Do

### 6.1 — Delete GlobalUsings alias

```
src/BodyCam.Tests/GlobalUsings.cs
```

**Delete entire file** (or remove the alias line):
```csharp
// DELETE: Resolve ambiguity: Microsoft.Extensions.AI 10.x introduces its own IRealtimeClient
global using IRealtimeClient = BodyCam.Services.IRealtimeClient;
```

The `BodyCam.Services.IRealtimeClient` type no longer exists. The only `IRealtimeClient` is now `Microsoft.Extensions.AI.IRealtimeClient`.

### 6.2 — Delete `RealtimeMessageTests.cs`

```
src/BodyCam.Tests/RealtimeMessageTests.cs
```

Delete — tests the hand-rolled JSON DTOs that no longer exist.

### 6.3 — Update `BodyCamTestHost.cs`

```
src/BodyCam.Tests/TestInfrastructure/BodyCamTestHost.cs
```

**Replace:**
```csharp
// Realtime client stub
services.AddSingleton(Substitute.For<IRealtimeClient>());
```

**With:**
```csharp
// MAF Realtime client stub
services.AddSingleton(Substitute.For<Microsoft.Extensions.AI.IRealtimeClient>());
```

**Remove `using BodyCam.Services;`** if `IRealtimeClient` was the only reason for it. Check if other types from that namespace are used.

### 6.4 — Update `AgentOrchestratorTests.cs`

```
src/BodyCam.Tests/Orchestration/AgentOrchestratorTests.cs
```

This is the most complex test update. The tests currently:
- Create `Substitute.For<IRealtimeClient>()` 
- Pass it to `VoiceInputAgent` constructor
- Pass it to `AgentOrchestrator` constructor
- Use `Raise.Event` to trigger event handlers
- Verify `realtime.Received(1).ConnectAsync(...)` etc.

**Updated `CreateOrchestrator`:**

```csharp
private static AgentOrchestrator CreateOrchestrator(
    out IAudioInputService audioIn,
    out IAudioOutputService audioOut,
    out Microsoft.Extensions.AI.IRealtimeClient realtimeFactory,
    out Microsoft.Extensions.AI.IRealtimeClientSession session,
    InAppLogSink? logSink = null)
{
    audioIn = Substitute.For<IAudioInputService>();
    audioOut = Substitute.For<IAudioOutputService>();

    // MAF mocks
    realtimeFactory = Substitute.For<Microsoft.Extensions.AI.IRealtimeClient>();
    session = Substitute.For<Microsoft.Extensions.AI.IRealtimeClientSession>();
    realtimeFactory.CreateSessionAsync(
        Arg.Any<RealtimeSessionOptions>(), Arg.Any<CancellationToken>())
        .Returns(Task.FromResult(session));

    // VoiceInputAgent no longer takes IRealtimeClient
    var voiceIn = new VoiceInputAgent(audioIn);
    var chatClient = Substitute.For<IChatClient>();
    var conversation = new ConversationAgent(chatClient, new AppSettings());
    var voiceOut = new VoiceOutputAgent(audioOut);
    var visionChatClient = Substitute.For<IChatClient>();
    var vision = new VisionAgent(visionChatClient, new AppSettings());
    var settingsService = Substitute.For<ISettingsService>();
    settingsService.RealtimeModel.Returns(ModelOptions.DefaultRealtime);
    settingsService.ChatModel.Returns(ModelOptions.DefaultChat);
    settingsService.VisionModel.Returns(ModelOptions.DefaultVision);
    settingsService.TranscriptionModel.Returns(ModelOptions.DefaultTranscription);
    settingsService.Voice.Returns(ModelOptions.DefaultVoice);
    settingsService.TurnDetection.Returns(ModelOptions.DefaultTurnDetection);
    settingsService.NoiseReduction.Returns(ModelOptions.DefaultNoiseReduction);
    settingsService.SystemInstructions.Returns("You are a helpful assistant.");
    settingsService.Provider.Returns(OpenAiProvider.OpenAi);
    settingsService.AzureApiVersion.Returns("2025-04-01-preview");

    var describeSceneTool = new DescribeSceneTool(vision);
    var deepAnalysisTool = new DeepAnalysisTool(conversation);
    var dispatcher = new ToolDispatcher(new ITool[] { describeSceneTool, deepAnalysisTool });
    var wakeWord = Substitute.For<IWakeWordService>();
    var micCoordinator = Substitute.For<IMicrophoneCoordinator>();
    var cameraManager = new CameraManager([], settingsService);
    var aec = new AecProcessor(NullLogger<AecProcessor>.Instance);
    var sink = logSink ?? new InAppLogSink();
    var loggerProvider = new InAppLoggerProvider(sink, LogLevel.Debug);
    var loggerFactory = new LoggerFactory([loggerProvider]);
    var logger = loggerFactory.CreateLogger<AgentOrchestrator>();

    return new AgentOrchestrator(
        voiceIn, conversation, voiceOut, vision,
        realtimeFactory,  // was: realtime
        settingsService, new AppSettings(), dispatcher,
        wakeWord, micCoordinator, cameraManager, aec, logger);
}
```

**Update individual tests:**

Tests that verified `realtime.Received(1).ConnectAsync(...)` should now verify `realtimeFactory.Received(1).CreateSessionAsync(...)`:

```csharp
[Fact]
public async Task StartAsync_CreatesSession()
{
    var orchestrator = CreateOrchestrator(out _, out _, out var factory, out var session);

    await orchestrator.StartAsync();

    await factory.Received(1).CreateSessionAsync(
        Arg.Any<RealtimeSessionOptions>(), Arg.Any<CancellationToken>());
    orchestrator.IsRunning.Should().BeTrue();

    await orchestrator.StopAsync();
}
```

Tests that used `Raise.Event` to test event routing need to be redesigned. Instead of raising events on the mock, **feed messages through the mock session's `GetStreamingResponseAsync()`**:

```csharp
// Example: test that audio deltas route to voice output
session.GetStreamingResponseAsync(Arg.Any<CancellationToken>())
    .Returns(AsyncEnumerable(new OutputTextAudioRealtimeServerMessage
    {
        Type = RealtimeServerMessageType.OutputAudioDelta,
        Audio = new DataContent(new byte[] { 1, 2, 3, 4 }, "audio/pcm")
    }));
```

> **Note:** The exact approach depends on how `IRealtimeClientSession.GetStreamingResponseAsync()` works. If it returns `IAsyncEnumerable<RealtimeServerMessage>`, create a helper that yields test messages.

Tests that verified `realtime.DisconnectAsync()` should now verify `session.DisposeAsync()`.

### 6.5 — Update `VoiceInputAgentTests.cs`

```
src/BodyCam.Tests/Agents/VoiceInputAgentTests.cs
```

**Current pattern:**
```csharp
var realtime = Substitute.For<IRealtimeClient>();
realtime.IsConnected.Returns(true);
var agent = new VoiceInputAgent(audioInput, realtime);
```

**New pattern:**
```csharp
var agent = new VoiceInputAgent(audioInput);
var sent = new List<byte[]>();
agent.SetAudioSink(async (pcm, ct) => sent.Add(pcm));
agent.SetConnected(true);
```

**Update each test:**

```csharp
[Fact]
public async Task StartAsync_StartsAudioInput()
{
    var audioInput = Substitute.For<IAudioInputService>();
    var agent = new VoiceInputAgent(audioInput);

    await agent.StartAsync();

    await audioInput.Received(1).StartAsync(Arg.Any<CancellationToken>());
}

[Fact]
public async Task StartAsync_SubscribesToAudioChunks()
{
    var audioInput = Substitute.For<IAudioInputService>();
    var agent = new VoiceInputAgent(audioInput);
    var sent = new List<byte[]>();
    agent.SetAudioSink(async (pcm, ct) => sent.Add(pcm));
    agent.SetConnected(true);

    await agent.StartAsync();

    audioInput.AudioChunkAvailable += Raise.Event<EventHandler<byte[]>>(
        audioInput, new byte[] { 1, 2, 3 });
    await Task.Delay(50);

    sent.Should().ContainSingle().Which.Should().BeEquivalentTo(new byte[] { 1, 2, 3 });
}

[Fact]
public async Task OnAudioChunk_WhenNotConnected_DoesNotSend()
{
    var audioInput = Substitute.For<IAudioInputService>();
    var agent = new VoiceInputAgent(audioInput);
    var sent = new List<byte[]>();
    agent.SetAudioSink(async (pcm, ct) => sent.Add(pcm));
    agent.SetConnected(false);  // not connected

    await agent.StartAsync();

    audioInput.AudioChunkAvailable += Raise.Event<EventHandler<byte[]>>(
        audioInput, new byte[] { 1, 2, 3 });
    await Task.Delay(50);

    sent.Should().BeEmpty();
}

[Fact]
public async Task StopAsync_UnsubscribesFromAudioChunks()
{
    var audioInput = Substitute.For<IAudioInputService>();
    var agent = new VoiceInputAgent(audioInput);
    var sent = new List<byte[]>();
    agent.SetAudioSink(async (pcm, ct) => sent.Add(pcm));
    agent.SetConnected(true);

    await agent.StartAsync();
    await agent.StopAsync();

    audioInput.AudioChunkAvailable += Raise.Event<EventHandler<byte[]>>(
        audioInput, new byte[] { 1 });
    await Task.Delay(50);

    sent.Should().BeEmpty();
}
```

### 6.6 — Remove `RealtimeClient` from all ToolContext initializers

**18 files** need `RealtimeClient = Substitute.For<IRealtimeClient>()` or similar removed:

| File | Line Pattern to Remove |
|---|---|
| `Tools/DescribeSceneToolTests.cs:26` | `RealtimeClient = Substitute.For<IRealtimeClient>()` |
| `Tools/DeepAnalysisToolTests.cs:26` | same |
| `Tools/ReadTextToolTests.cs:26` | same |
| `Tools/TakePhotoToolTests.cs:16` | same |
| `Tools/SaveMemoryToolTests.cs:16` | same |
| `Tools/RecallMemoryToolTests.cs:16` | same |
| `Tools/SetTranslationModeToolTests.cs:16` | same |
| `Tools/MakePhoneCallToolTests.cs:16` | same |
| `Tools/SendMessageToolTests.cs:16` | same |
| `Tools/LookupAddressToolTests.cs:16` | same |
| `Tools/FindObjectToolTests.cs:17` | same |
| `Tools/NavigateToToolTests.cs:15` | same |
| `Tools/StartSceneWatchToolTests.cs:17` | same |
| `Tools/ToolBaseTests.cs:59` | same |
| `Tools/ToolDispatcherTests.cs:29` | `RealtimeClient = NSubstitute.Substitute.For<BodyCam.Services.IRealtimeClient>()` |
| `Integration/ToolPipelineTests.cs:22` | `RealtimeClient = _host.Services...` |
| `Integration/BodyCamTestHostTests.cs:92` | `RealtimeClient = _host.Services...` |
| `Integration/CrossCuttingTests.cs:56,318` | `RealtimeClient = _host.Services...` |
| `Integration/MemoryToolTests.cs:23` | `RealtimeClient = _host.Services...` |

**For each file**, remove the `RealtimeClient = ...` line from the `ToolContext` initializer. Also remove `using BodyCam.Services;` if it was only needed for `IRealtimeClient`.

This is a mechanical find-and-remove operation:
```powershell
# Find all occurrences to update
Get-ChildItem src/BodyCam.Tests -Recurse -Filter *.cs |
    Select-String "RealtimeClient\s*=" |
    Format-Table Path, LineNumber, Line -AutoSize
```

### 6.7 — Run full test suite

```powershell
dotnet test src/BodyCam.Tests/
```

All tests must pass. Expected count: ~370 tests (minus deleted `RealtimeMessageTests`).

---

## Acceptance Criteria

- [ ] `GlobalUsings.cs` alias deleted
- [ ] `RealtimeMessageTests.cs` deleted
- [ ] `BodyCamTestHost.cs` registers MAF mock
- [ ] `AgentOrchestratorTests.cs` uses MAF mocks + session-based testing
- [ ] `VoiceInputAgentTests.cs` uses delegate-based testing
- [ ] All 18 tool test files updated — `RealtimeClient` removed from ToolContext
- [ ] Full test suite passes
- [ ] No reference to `BodyCam.Services.IRealtimeClient` anywhere in `src/BodyCam.Tests/`
