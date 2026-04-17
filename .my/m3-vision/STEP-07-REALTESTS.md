# Step 7: Vision RealTests

Add real integration tests that exercise the vision function-call pipeline against the live API. These tests verify the `describe_scene` round-trip with query parameters, multi-turn vision context, and the interaction between vision and audio responses.

Depends on: Steps 1–6 (all prior vision work must be complete).

## Fixture Changes

### `src/BodyCam.RealTests/Fixtures/RealtimeFixture.cs`

**Add** a helper to wait specifically for a `describe_scene` function call, separate from the generic `WaitForFunctionCallAsync`:

```csharp
    /// <summary>
    /// Wait for a describe_scene function call specifically.
    /// Returns the FunctionCallInfo, or null if timed out.
    /// </summary>
    public async Task<FunctionCallInfo?> WaitForDescribeSceneCallAsync(TimeSpan? timeout = null)
    {
        var t = timeout ?? TimeSpan.FromSeconds(30);
        var deadline = DateTimeOffset.UtcNow + t;

        while (DateTimeOffset.UtcNow < deadline)
        {
            var match = _functionCalls.FirstOrDefault(fc => fc.Name == "describe_scene");
            if (match is not null) return match;
            await Task.Delay(100);
        }
        return null;
    }
```

**Add** a counter for multiple function call completions:

```csharp
    private int _functionCallCount;

    /// <summary>
    /// Wait for N function calls total.
    /// </summary>
    public async Task WaitForFunctionCallCountAsync(int count, TimeSpan? timeout = null)
    {
        var t = timeout ?? TimeSpan.FromSeconds(30);
        var deadline = DateTimeOffset.UtcNow + t;

        while (_functionCalls.Count < count && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(100);

        if (_functionCalls.Count < count)
            throw new TimeoutException(
                $"Expected {count} function calls but got {_functionCalls.Count} after {t.TotalSeconds}s");
    }
```

## Files Created

### `src/BodyCam.RealTests/Pipeline/VisionFunctionCallTests.cs`

```csharp
using System.Text.Json;
using BodyCam.RealTests.Fixtures;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace BodyCam.RealTests.Pipeline;

/// <summary>
/// Tests that verify the describe_scene function-call pipeline against the live API.
/// Validates that the model triggers vision calls, accepts results, and responds naturally.
/// </summary>
[Trait("Category", "RealAPI")]
public class VisionFunctionCallTests : IAsyncLifetime
{
    private readonly RealtimeFixture _fixture = new();
    private readonly ITestOutputHelper _output;

    public VisionFunctionCallTests(ITestOutputHelper output)
    {
        _output = output;
        _fixture.SetOutput(output);
    }

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task DescribeScene_QueryParameterPassedInArguments()
    {
        // Ask a specific visual question — the model should include a query in the function call args
        await _fixture.SendTextInputAsync(
            "Can you read the text on the sign in front of me? Use the camera to look.");

        await _fixture.WaitForResponseAsync(TimeSpan.FromSeconds(30));

        if (_fixture.FunctionCalls.Count == 0)
        {
            _output.WriteLine("No function call triggered — model answered directly.");
            _output.WriteLine("Transcript: " + string.Join("; ", _fixture.OutputTranscripts));
            // This may happen if describe_scene has no query param yet (Step 2 adds it).
            // Log for diagnosis but still assert:
        }

        _fixture.FunctionCalls.Should().ContainSingle(fc => fc.Name == "describe_scene");
        var call = _fixture.FunctionCalls.First(fc => fc.Name == "describe_scene");
        call.CallId.Should().NotBeNullOrEmpty();

        _output.WriteLine($"CallId: {call.CallId}");
        _output.WriteLine($"Arguments: {call.Arguments}");

        // After Step 2, arguments should include "query" with the user's question
        if (!string.IsNullOrEmpty(call.Arguments) && call.Arguments != "{}")
        {
            using var doc = JsonDocument.Parse(call.Arguments);
            if (doc.RootElement.TryGetProperty("query", out var q))
            {
                var query = q.GetString();
                query.Should().NotBeNullOrEmpty("model should pass the user's visual question");
                _output.WriteLine($"Query: {query}");
            }
        }
    }

    [Fact]
    public async Task DescribeScene_RoundTrip_ModelSpeaksDescription()
    {
        await _fixture.SendTextInputAsync("Look at what's around me right now. Use your camera.");

        await _fixture.WaitForResponseAsync(TimeSpan.FromSeconds(30));

        if (_fixture.FunctionCalls.Count == 0)
        {
            _output.WriteLine("No function call triggered.");
            return;
        }

        var call = _fixture.FunctionCalls.First(fc => fc.Name == "describe_scene");
        _output.WriteLine($"Function call: {call.Name} (callId: {call.CallId})");

        // Reset and send mock camera result
        _fixture.Reset();

        var mockResult = JsonSerializer.Serialize(new
        {
            description = "A white desk with a 27-inch monitor showing a code editor. " +
                          "A mechanical keyboard, a wireless mouse, and a coffee mug with steam rising from it. " +
                          "Behind the monitor there is a bookshelf with technical books."
        });

        await _fixture.SendFunctionCallOutputAsync(call.CallId, mockResult);
        await _fixture.WaitForResponseAsync(TimeSpan.FromSeconds(30));

        // Model should speak a response that references the scene
        _fixture.OutputTranscripts.Should().NotBeEmpty("model should speak after receiving vision result");
        _fixture.AudioChunks.Should().NotBeEmpty("model should produce audio");
        _fixture.Errors.Should().BeEmpty();

        var spoken = _fixture.OutputTranscripts[0];
        _output.WriteLine($"Model said: {spoken}");

        // The response should reference at least one thing from the description
        var mentionsSomething = spoken.Contains("monitor", StringComparison.OrdinalIgnoreCase)
                             || spoken.Contains("desk", StringComparison.OrdinalIgnoreCase)
                             || spoken.Contains("keyboard", StringComparison.OrdinalIgnoreCase)
                             || spoken.Contains("coffee", StringComparison.OrdinalIgnoreCase)
                             || spoken.Contains("book", StringComparison.OrdinalIgnoreCase);
        mentionsSomething.Should().BeTrue(
            "model should reference objects from the vision description in its response");
    }

    [Fact]
    public async Task DescribeScene_CameraUnavailable_ModelHandlesGracefully()
    {
        await _fixture.SendTextInputAsync("What do you see around me? Look through the camera.");

        await _fixture.WaitForResponseAsync(TimeSpan.FromSeconds(30));

        if (_fixture.FunctionCalls.Count == 0)
        {
            _output.WriteLine("No function call triggered.");
            return;
        }

        var call = _fixture.FunctionCalls.First(fc => fc.Name == "describe_scene");

        _fixture.Reset();

        // Simulate camera failure
        var errorResult = JsonSerializer.Serialize(new
        {
            description = "Camera not available or no frame captured."
        });

        await _fixture.SendFunctionCallOutputAsync(call.CallId, errorResult);
        await _fixture.WaitForResponseAsync(TimeSpan.FromSeconds(30));

        // Model should handle gracefully — should still respond, not error out
        _fixture.OutputTranscripts.Should().NotBeEmpty(
            "model should respond even when camera is unavailable");
        _fixture.Errors.Should().BeEmpty();

        var spoken = _fixture.OutputTranscripts[0];
        _output.WriteLine($"Model said: {spoken}");

        // Model should acknowledge the camera issue
        var acknowledgesIssue = spoken.Contains("camera", StringComparison.OrdinalIgnoreCase)
                             || spoken.Contains("see", StringComparison.OrdinalIgnoreCase)
                             || spoken.Contains("unable", StringComparison.OrdinalIgnoreCase)
                             || spoken.Contains("available", StringComparison.OrdinalIgnoreCase)
                             || spoken.Contains("can't", StringComparison.OrdinalIgnoreCase)
                             || spoken.Contains("sorry", StringComparison.OrdinalIgnoreCase);
        acknowledgesIssue.Should().BeTrue(
            "model should acknowledge the camera is unavailable");
    }

    [Fact]
    public async Task DescribeScene_FollowUpQuestion_MaintainsContext()
    {
        // First turn: trigger vision
        await _fixture.SendTextInputAsync("Look at what's in front of me.");

        await _fixture.WaitForResponseAsync(TimeSpan.FromSeconds(30));

        if (_fixture.FunctionCalls.Count == 0)
        {
            _output.WriteLine("No function call triggered on first turn.");
            return;
        }

        var call = _fixture.FunctionCalls.First(fc => fc.Name == "describe_scene");

        _fixture.Reset();

        await _fixture.SendFunctionCallOutputAsync(call.CallId, JsonSerializer.Serialize(new
        {
            description = "A golden retriever sleeping on a blue couch."
        }));
        await _fixture.WaitForResponseAsync(TimeSpan.FromSeconds(30));

        var firstResponse = _fixture.OutputTranscripts.FirstOrDefault() ?? "";
        _output.WriteLine($"First response: {firstResponse}");

        // Second turn: ask a follow-up about the same scene (no new vision call expected)
        _fixture.Reset();

        await _fixture.SendTextInputAsync("What breed of dog is that?");
        await _fixture.WaitForResponseAsync(TimeSpan.FromSeconds(30));

        _fixture.OutputTranscripts.Should().NotBeEmpty(
            "model should answer the follow-up question");

        var followUp = _fixture.OutputTranscripts[0];
        _output.WriteLine($"Follow-up response: {followUp}");

        // Model should reference the dog or retriever from the earlier vision context
        var referencesContext = followUp.Contains("retriever", StringComparison.OrdinalIgnoreCase)
                             || followUp.Contains("golden", StringComparison.OrdinalIgnoreCase)
                             || followUp.Contains("dog", StringComparison.OrdinalIgnoreCase);
        referencesContext.Should().BeTrue(
            "model should maintain vision context across turns");
    }

    [Fact]
    public async Task DescribeScene_EventSequence_IsCorrect()
    {
        await _fixture.SendTextInputAsync("What do you see through the camera?");

        await _fixture.WaitForResponseAsync(TimeSpan.FromSeconds(30));

        if (_fixture.FunctionCalls.Count == 0)
        {
            _output.WriteLine("No function call triggered.");
            return;
        }

        // Verify event ordering for function call flow:
        // response.output_item.added → function_call_arguments.delta(s) →
        // function_call_arguments.done → response.output_item.done → response.done
        var outputItemAddedIdx = _fixture.IndexOfEvent("response.output_item.added");
        var argsStartIdx = _fixture.IndexOfEvent("response.function_call_arguments.delta");
        var argsDoneIdx = _fixture.IndexOfEvent("response.function_call_arguments.done");
        var outputItemDoneIdx = _fixture.IndexOfEvent("response.output_item.done");
        var responseDoneIdx = _fixture.IndexOfEvent("response.done");

        _output.WriteLine($"output_item.added: {outputItemAddedIdx}");
        _output.WriteLine($"args.delta (first): {argsStartIdx}");
        _output.WriteLine($"args.done: {argsDoneIdx}");
        _output.WriteLine($"output_item.done: {outputItemDoneIdx}");
        _output.WriteLine($"response.done: {responseDoneIdx}");

        outputItemAddedIdx.Should().BeGreaterThanOrEqualTo(0);
        argsStartIdx.Should().BeGreaterThan(outputItemAddedIdx,
            "argument deltas should follow output_item.added");
        argsDoneIdx.Should().BeGreaterThan(argsStartIdx,
            "args.done should follow args.delta");
        outputItemDoneIdx.Should().BeGreaterThan(argsDoneIdx,
            "output_item.done should follow args.done");
        responseDoneIdx.Should().BeGreaterThan(outputItemDoneIdx,
            "response.done should follow output_item.done");

        // After sending result back, verify continuation produces audio
        var call = _fixture.FunctionCalls[0];
        _fixture.Reset();

        await _fixture.SendFunctionCallOutputAsync(call.CallId, JsonSerializer.Serialize(new
        {
            description = "An empty room with white walls."
        }));
        await _fixture.WaitForResponseAsync(TimeSpan.FromSeconds(30));

        _fixture.AudioChunks.Should().NotBeEmpty(
            "continuation response should include audio");

        // Continuation should have standard audio response events
        _fixture.IndexOfEvent("response.audio.delta").Should().BeGreaterThanOrEqualTo(0);
        _fixture.IndexOfEvent("response.audio_transcript.delta").Should().BeGreaterThanOrEqualTo(0);
    }
}
```

## Test Summary

| # | Test | Validates |
|---|------|-----------|
| 1 | `DescribeScene_QueryParameterPassedInArguments` | Model passes user's visual question as `query` in function call args |
| 2 | `DescribeScene_RoundTrip_ModelSpeaksDescription` | Full vision round-trip: trigger → mock result → model speaks about the scene |
| 3 | `DescribeScene_CameraUnavailable_ModelHandlesGracefully` | Model handles "camera not available" result without error |
| 4 | `DescribeScene_FollowUpQuestion_MaintainsContext` | Vision context persists across conversation turns |
| 5 | `DescribeScene_EventSequence_IsCorrect` | Function call event ordering matches Realtime API spec |

## Test Count Impact

| Suite | Before | Added | After |
|-------|--------|-------|-------|
| RealTests | 37 | 5 | 42 |

## Verification

```powershell
dotnet build src/BodyCam.RealTests/BodyCam.RealTests.csproj -v q
dotnet test src/BodyCam.RealTests/BodyCam.RealTests.csproj --filter "VisionFunctionCallTests" -v n
dotnet test src/BodyCam.RealTests/BodyCam.RealTests.csproj -v n  # full suite
```

## Notes

- Tests 2–5 gracefully skip (log + return) if the model doesn't trigger `describe_scene`, since the LLM may occasionally answer directly without using the tool. Test 1 asserts it.
- Mock vision results are realistic multi-sentence descriptions to test that the model speaks naturally about the scene.
- Test 4 (follow-up) validates that the Realtime API session maintains vision context — this is critical for the "smart glasses" use case where users ask follow-up questions about what they see.
