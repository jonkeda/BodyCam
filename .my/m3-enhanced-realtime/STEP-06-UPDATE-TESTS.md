# Step 6: Update Tests

Update all test files to reflect M3 changes: remove Mode B tests, update ConversationAgent tests for new `AnalyzeAsync` signature, add function calling tests, update VisionAgent tests.

## Depends On: Steps 1–5

## Files Modified

### 1. `src/BodyCam.Tests/Agents/ConversationAgentTests.cs`

**Rewrite** for the new `AnalyzeAsync` API:

```csharp
using BodyCam.Agents;
using FluentAssertions;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace BodyCam.Tests.Agents;

public class ConversationAgentTests
{
    private readonly IChatClient _chatClient = Substitute.For<IChatClient>();
    private readonly AppSettings _settings = new();
    private readonly ConversationAgent _agent;

    public ConversationAgentTests()
    {
        _agent = new ConversationAgent(_chatClient, _settings);
    }

    [Fact]
    public async Task AnalyzeAsync_ReturnsModelResponse()
    {
        _chatClient.GetResponseAsync(
            Arg.Any<IList<ChatMessage>>(),
            Arg.Any<ChatOptions?>(),
            Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Analysis result")));

        var result = await _agent.AnalyzeAsync("What is 2+2?");

        result.Should().Be("Analysis result");
    }

    [Fact]
    public async Task AnalyzeAsync_IncludesContextWhenProvided()
    {
        IList<ChatMessage>? capturedMessages = null;
        _chatClient.GetResponseAsync(
            Arg.Do<IList<ChatMessage>>(m => capturedMessages = m),
            Arg.Any<ChatOptions?>(),
            Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));

        await _agent.AnalyzeAsync("query", "some context");

        capturedMessages.Should().NotBeNull();
        capturedMessages!.Should().Contain(m =>
            m.Role == ChatRole.System &&
            m.Text != null && m.Text.Contains("some context"));
    }

    [Fact]
    public async Task AnalyzeAsync_IncludesSystemPrompt()
    {
        IList<ChatMessage>? capturedMessages = null;
        _chatClient.GetResponseAsync(
            Arg.Do<IList<ChatMessage>>(m => capturedMessages = m),
            Arg.Any<ChatOptions?>(),
            Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));

        await _agent.AnalyzeAsync("query");

        capturedMessages.Should().NotBeNull();
        capturedMessages![0].Role.Should().Be(ChatRole.System);
    }

    [Fact]
    public async Task AnalyzeAsync_ReturnsEmpty_WhenModelReturnsNull()
    {
        _chatClient.GetResponseAsync(
            Arg.Any<IList<ChatMessage>>(),
            Arg.Any<ChatOptions?>(),
            Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, (string?)null)));

        var result = await _agent.AnalyzeAsync("query");

        result.Should().BeEmpty();
    }
}
```

**Tests removed:**
- `AddUserMessage_AppendsToSession` — no longer exists
- `AddAssistantMessage_AppendsToSession` — no longer exists
- `ProcessTranscriptAsync_StreamsTokens` — Mode B removed
- `ProcessTranscriptAsync_AddsUserAndAssistantMessages` — Mode B removed
- `ProcessTranscriptAsync_SetsSystemPromptIfEmpty` — Mode B removed
- `ProcessTranscriptFullAsync_ReturnsCompleteReply` — Mode B removed

### 2. `src/BodyCam.Tests/Agents/VisionAgentTests.cs`

**Update** for new constructor (takes `IChatClient`):

```csharp
using BodyCam.Agents;
using BodyCam.Services;
using FluentAssertions;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace BodyCam.Tests.Agents;

public class VisionAgentTests
{
    [Fact]
    public async Task DescribeFrameAsync_ReturnsModelDescription()
    {
        var camera = Substitute.For<ICameraService>();
        var chatClient = Substitute.For<IChatClient>();
        chatClient.GetResponseAsync(
            Arg.Any<IList<ChatMessage>>(),
            Arg.Any<ChatOptions?>(),
            Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, "A desk with a laptop")));
        var settings = new AppSettings();
        var agent = new VisionAgent(camera, chatClient, settings);

        var result = await agent.DescribeFrameAsync([0xFF, 0xD8]);

        result.Should().Be("A desk with a laptop");
    }

    [Fact]
    public async Task CaptureAndDescribeAsync_ReturnsNull_WhenNoFrame()
    {
        var camera = Substitute.For<ICameraService>();
        camera.CaptureFrameAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<byte[]?>(null));
        var chatClient = Substitute.For<IChatClient>();
        var settings = new AppSettings();
        var agent = new VisionAgent(camera, chatClient, settings);

        var result = await agent.CaptureAndDescribeAsync();

        result.Should().BeNull();
    }

    [Fact]
    public async Task CaptureAndDescribeAsync_ReturnsDescription_WhenFrameAvailable()
    {
        var camera = Substitute.For<ICameraService>();
        camera.CaptureFrameAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<byte[]?>(new byte[] { 0xFF, 0xD8 }));
        var chatClient = Substitute.For<IChatClient>();
        chatClient.GetResponseAsync(
            Arg.Any<IList<ChatMessage>>(),
            Arg.Any<ChatOptions?>(),
            Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, "A park scene")));
        var settings = new AppSettings();
        var agent = new VisionAgent(camera, chatClient, settings);

        var result = await agent.CaptureAndDescribeAsync();

        result.Should().NotBeNull();
        result.Should().Be("A park scene");
    }
}
```

### 3. `src/BodyCam.Tests/Orchestration/AgentOrchestratorTests.cs`

**Update** `CreateOrchestrator` helper — `ConversationAgent` now takes `IChatClient`:

```csharp
// BEFORE:
var chatClient = Substitute.For<IChatCompletionsClient>();
var conversation = new ConversationAgent(chatClient, new AppSettings());

// AFTER:
var chatClient = Substitute.For<IChatClient>();
var conversation = new ConversationAgent(chatClient, new AppSettings());
```

**Update** VisionAgent construction — now takes `IChatClient`:

```csharp
// BEFORE:
var vision = new VisionAgent(camera, new AppSettings());

// AFTER:
var visionChatClient = Substitute.For<IChatClient>();
var vision = new VisionAgent(camera, visionChatClient, new AppSettings());
```

**Remove** `settingsService.Mode` line if present.

**Update** using statements — replace `BodyCam.Services` with `Microsoft.Extensions.AI` for `IChatClient`.

### 4. `src/BodyCam.Tests/Services/RealtimeMessageTests.cs`

**Add** function calling message tests:

```csharp
[Fact]
public void SessionUpdateMessage_WithTools_SerializesCorrectly()
{
    var msg = new SessionUpdateMessage
    {
        Type = "session.update",
        Session = new SessionUpdatePayload
        {
            Modalities = ["text", "audio"],
            Tools = [
                new ToolDefinition
                {
                    Name = "describe_scene",
                    Description = "Look at the camera",
                    Parameters = JsonDocument.Parse("""{"type":"object","properties":{},"required":[]}""").RootElement
                }
            ],
            ToolChoice = "auto"
        }
    };

    var json = JsonSerializer.Serialize(msg, RealtimeJsonContext.Default.SessionUpdateMessage);

    json.Should().Contain("\"tools\":");
    json.Should().Contain("\"name\":\"describe_scene\"");
    json.Should().Contain("\"tool_choice\":\"auto\"");
}

[Fact]
public void FunctionCallOutputMessage_SerializesCorrectly()
{
    var msg = new FunctionCallOutputMessage
    {
        Type = "conversation.item.create",
        Item = new FunctionCallOutputItem
        {
            CallId = "call_abc123",
            Output = "{\"description\":\"A desk with a laptop\"}"
        }
    };

    var json = JsonSerializer.Serialize(msg, RealtimeJsonContext.Default.FunctionCallOutputMessage);

    json.Should().Contain("\"type\":\"conversation.item.create\"");
    json.Should().Contain("\"call_id\":\"call_abc123\"");
    json.Should().Contain("\"output\":");
}

[Fact]
public void ServerEventParser_ParseFunctionCalls_ExtractsFromResponseDone()
{
    var json = """
    {
        "type": "response.done",
        "response": {
            "id": "resp_123",
            "output": [
                {
                    "type": "function_call",
                    "call_id": "call_abc",
                    "name": "describe_scene",
                    "arguments": "{}"
                }
            ]
        }
    }
    """;

    var calls = ServerEventParser.ParseFunctionCalls(json);

    calls.Should().HaveCount(1);
    calls[0].callId.Should().Be("call_abc");
    calls[0].name.Should().Be("describe_scene");
    calls[0].arguments.Should().Be("{}");
}

[Fact]
public void ServerEventParser_ParseFunctionCalls_ReturnsEmpty_WhenNoFunctionCalls()
{
    var json = """
    {
        "type": "response.done",
        "response": {
            "id": "resp_123",
            "output": [
                {
                    "type": "message",
                    "role": "assistant"
                }
            ]
        }
    }
    """;

    var calls = ServerEventParser.ParseFunctionCalls(json);

    calls.Should().BeEmpty();
}

[Fact]
public void DispatchMessage_ResponseDone_WithFunctionCall_FiresFunctionCallReceived()
{
    var apiKey = Substitute.For<IApiKeyService>();
    var settings = new AppSettings();
    var client = new RealtimeClient(apiKey, settings);

    FunctionCallInfo? received = null;
    client.FunctionCallReceived += (_, info) => received = info;

    var json = """
    {
        "type": "response.done",
        "response": {
            "id": "resp_123",
            "output": [
                {
                    "type": "function_call",
                    "call_id": "call_xyz",
                    "name": "deep_analysis",
                    "arguments": "{\"query\":\"explain quantum computing\"}"
                }
            ]
        }
    }
    """;
    client.DispatchMessage(json);

    received.Should().NotBeNull();
    received!.CallId.Should().Be("call_xyz");
    received.Name.Should().Be("deep_analysis");
    received.Arguments.Should().Contain("quantum computing");
}
```

**Remove** any tests referencing `ResponseCreateMessage` or `SendTextForTtsAsync` (if present).

### 5. `src/BodyCam.Tests/AppSettingsTests.cs`

No changes needed — the `ConversationMode` enum tests don't exist in this file.

### 6. `src/BodyCam.Tests/ModelOptionsTests.cs`

**Remove** any test referencing `ConversationModes` if present. The `AllArrays_AreNonEmpty` test references `ConversationModes` indirectly — check and update.

Current `AllArrays_AreNonEmpty` test does not reference `ConversationModes` directly, so no change needed.

## Verification

```powershell
dotnet build src/BodyCam.Tests/BodyCam.Tests.csproj -v q
dotnet test src/BodyCam.Tests/BodyCam.Tests.csproj -v q
```

All tests should pass. Expected test count will decrease slightly (Mode B tests removed) but new function calling tests add coverage.
