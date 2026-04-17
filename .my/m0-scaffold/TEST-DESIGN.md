# M0 — Test Design

## Overview

Two test projects covering unit tests (service/agent/viewmodel logic) and integration tests (HTTP API mocking with WireMock.Net for OpenAI interactions).

---

## Project Structure

```
src/
├── BodyCam/                          # Main app
├── BodyCam.Tests/                    # Unit tests
│   ├── Mvvm/
│   │   ├── ObservableObjectTests.cs
│   │   ├── RelayCommandTests.cs
│   │   └── AsyncRelayCommandTests.cs
│   ├── ViewModels/
│   │   └── MainViewModelTests.cs
│   ├── Agents/
│   │   ├── VoiceInputAgentTests.cs
│   │   ├── ConversationAgentTests.cs
│   │   ├── VoiceOutputAgentTests.cs
│   │   └── VisionAgentTests.cs
│   ├── Orchestration/
│   │   └── AgentOrchestratorTests.cs
│   └── Models/
│       └── SessionContextTests.cs
│
└── BodyCam.IntegrationTests/         # Integration tests (WireMock.Net)
    ├── Fixtures/
    │   └── OpenAiWireMockFixture.cs
    ├── Services/
    │   └── OpenAiStreamingClientTests.cs
    └── Orchestration/
        └── FullPipelineTests.cs
```

---

## Tech Stack

| Package | Version | Purpose |
|---------|---------|---------|
| xUnit | latest | Test framework |
| xUnit.runner.visualstudio | latest | VS test runner |
| NSubstitute | latest | Interface mocking for unit tests |
| FluentAssertions | latest | Readable assertions |
| WireMock.Net | 2.2.0 | HTTP/WebSocket mock server for integration tests |
| Microsoft.NET.Test.Sdk | latest | Test host |

---

## Unit Tests — Strategy

Unit tests mock all dependencies via **NSubstitute** on the interfaces we defined. No real network, no real audio, no real camera.

### Mvvm Layer

**ObservableObjectTests:**
- PropertyChanged fires on SetProperty with new value
- PropertyChanged does NOT fire on SetProperty with same value
- SetProperty returns true on change, false on no-op

**RelayCommandTests:**
- Execute calls action
- CanExecute returns true by default
- CanExecute respects provided predicate
- RaiseCanExecuteChanged fires event

**AsyncRelayCommandTests:**
- Execute runs async action
- CanExecute returns false while executing (re-entrancy guard)
- IsExecuting toggles correctly
- RaiseCanExecuteChanged fires event

### ViewModels

**MainViewModelTests:**
```csharp
// Arrange
var orchestrator = Substitute.For<AgentOrchestrator>(...);
var vm = new MainViewModel(orchestrator);

// Act & Assert
Assert.Equal("Start", vm.ToggleButtonText);
Assert.False(vm.IsRunning);

// Toggle on
vm.ToggleCommand.Execute(null);
Assert.True(vm.IsRunning);
Assert.Equal("Stop", vm.ToggleButtonText);
```

**Test cases:**
- Initial state: not running, button says "Start"
- Toggle on: calls orchestrator.StartAsync, button says "Stop"
- Toggle off: calls orchestrator.StopAsync, button says "Start"
- TranscriptUpdated event appends to Transcript property
- DebugLog event appends with timestamp

### Agents

**ConversationAgentTests:**
```csharp
var settings = new AppSettings { ChatModel = "gpt-5.4-mini" };
var agent = new ConversationAgent(settings);
var session = new SessionContext();

var reply = await agent.ProcessAsync("Hello", session);

Assert.Contains("Echo: Hello", reply);  // stub behavior
Assert.Equal(1, session.Messages.Count(m => m.Role == "assistant"));
```

**VoiceInputAgentTests:**
- Subscribes to AudioChunkAvailable
- Sends chunks to OpenAI client
- Emits TranscriptReceived for each transcript

**VoiceOutputAgentTests:**
- Calls SynthesizeStreamingAsync with text
- Plays each chunk via AudioOutputService

**VisionAgentTests:**
- DescribeFrameAsync returns stub string
- CaptureAndDescribeAsync returns null when camera returns null

### Orchestration

**AgentOrchestratorTests:**
- StartAsync connects OpenAI, starts voice input
- StopAsync disconnects, cancels
- TranscriptReceived → triggers ConversationAgent → triggers VoiceOutput
- DebugLog events fire at each step
- Double-start is no-op
- Stop when not running is no-op

### Models

**SessionContextTests:**
- New session gets unique ID
- Messages list starts empty
- Adding messages preserves order

---

## Integration Tests — WireMock.Net Strategy

Integration tests spin up a local **WireMock.Net** server that mimics the OpenAI API. Real HTTP calls are made, but against the mock server.

### OpenAiWireMockFixture

Shared test fixture that starts a WireMock server and configures it with OpenAI-like endpoints.

```csharp
public class OpenAiWireMockFixture : IAsyncLifetime
{
    public WireMockServer Server { get; private set; } = null!;
    public string BaseUrl => Server.Url!;

    public Task InitializeAsync()
    {
        Server = WireMockServer.Start();
        SetupDefaultStubs();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        Server.Stop();
        Server.Dispose();
        return Task.CompletedTask;
    }

    private void SetupDefaultStubs()
    {
        // Chat Completions endpoint
        Server
            .Given(Request.Create()
                .WithPath("/v1/chat/completions")
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""
                {
                    "id": "chatcmpl-test",
                    "object": "chat.completion",
                    "choices": [{
                        "index": 0,
                        "message": {
                            "role": "assistant",
                            "content": "Mock response from WireMock"
                        },
                        "finish_reason": "stop"
                    }],
                    "usage": {
                        "prompt_tokens": 10,
                        "completion_tokens": 5,
                        "total_tokens": 15
                    }
                }
                """));

        // Vision endpoint (same path, detected by image_url in body)
        Server
            .Given(Request.Create()
                .WithPath("/v1/chat/completions")
                .UsingPost()
                .WithBody(new JsonPartialMatcher("\"image_url\"")))
            .AtPriority(1)
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""
                {
                    "id": "chatcmpl-vision",
                    "object": "chat.completion",
                    "choices": [{
                        "index": 0,
                        "message": {
                            "role": "assistant",
                            "content": "I see a desk with a laptop and coffee mug."
                        },
                        "finish_reason": "stop"
                    }],
                    "usage": {
                        "prompt_tokens": 200,
                        "completion_tokens": 12,
                        "total_tokens": 212
                    }
                }
                """));
    }

    /// <summary>
    /// Stub a specific error response for resilience testing.
    /// </summary>
    public void StubRateLimit()
    {
        Server
            .Given(Request.Create()
                .WithPath("/v1/chat/completions")
                .UsingPost())
            .AtPriority(0)
            .RespondWith(Response.Create()
                .WithStatusCode(429)
                .WithHeader("Retry-After", "1")
                .WithBody("""{"error":{"message":"Rate limit exceeded"}}"""));
    }

    /// <summary>
    /// Stub a server error for error handling tests.
    /// </summary>
    public void StubServerError()
    {
        Server
            .Given(Request.Create()
                .WithPath("/v1/chat/completions")
                .UsingPost())
            .AtPriority(0)
            .RespondWith(Response.Create()
                .WithStatusCode(500)
                .WithBody("""{"error":{"message":"Internal server error"}}"""));
    }
}
```

### OpenAiStreamingClientTests

```csharp
public class OpenAiStreamingClientTests : IClassFixture<OpenAiWireMockFixture>
{
    private readonly OpenAiWireMockFixture _fixture;

    public OpenAiStreamingClientTests(OpenAiWireMockFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ChatCompletion_ReturnsResponse()
    {
        // Arrange — point client at WireMock
        var settings = new AppSettings
        {
            OpenAiApiKey = "test-key",
            RealtimeApiEndpoint = _fixture.BaseUrl
        };
        var client = new OpenAiStreamingClient(settings);

        // Act — call the mock OpenAI
        // (once OpenAiStreamingClient has real HTTP chat method)

        // Assert
        // Verify response matches mock
        // Verify WireMock received expected request
        var logs = _fixture.Server.LogEntries;
        Assert.NotEmpty(logs);
    }

    [Fact]
    public async Task RateLimit_RetriesOrThrows()
    {
        _fixture.StubRateLimit();
        // Test retry / error handling behavior
    }
}
```

### FullPipelineTests

End-to-end test of the orchestrator with real DI but mocked OpenAI:

```csharp
public class FullPipelineTests : IClassFixture<OpenAiWireMockFixture>
{
    [Fact]
    public async Task Orchestrator_ProcessesTranscript_ReturnsResponse()
    {
        // Arrange: build full DI container with WireMock URL
        // Register stub audio services (no real mic/speaker)
        // Register real ConversationAgent pointed at WireMock

        // Act: simulate transcript event
        // Assert: orchestrator produces AI response
    }
}
```

---

## WireMock.Net Stubs Needed (Per Milestone)

| Milestone | Endpoint | Stub |
|-----------|----------|------|
| M1 | `wss://*/v1/realtime` | WebSocket mock (audio in/out) |
| M2 | `POST /v1/chat/completions` | Chat response |
| M3 | `POST /v1/chat/completions` (vision) | Vision response with image_url |
| M5 | `POST /v1/chat/completions` | Translation response |

> **Note:** WireMock.Net does not natively mock WebSockets. For M1 Realtime API tests, we'll either:
> 1. Create a thin WebSocket test server alongside WireMock, or
> 2. Test the WebSocket layer with a custom `TestWebSocketServer` class and use WireMock for REST-only paths.

---

## Test Conventions

| Convention | Rule |
|------------|------|
| Naming | `MethodName_Scenario_ExpectedResult` |
| Arrange-Act-Assert | Every test follows AAA |
| One assert per test | Prefer focused assertions |
| No test interdependency | Tests run in any order |
| Collection fixtures | WireMock server shared per collection |
| Async tests | Use `async Task`, not `async void` |

---

## Running Tests

```bash
# Unit tests
dotnet test src/BodyCam.Tests

# Integration tests
dotnet test src/BodyCam.IntegrationTests

# All tests
dotnet test
```

---

## CI Integration

Both test projects will be added to a future solution file (`BodyCam.sln`) and run in CI via `dotnet test`. WireMock.Net is self-contained — no external services needed.
