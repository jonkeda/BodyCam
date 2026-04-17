using System.Text.Json;
using BodyCam.Models;
using BodyCam.RealTests.Fixtures;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace BodyCam.RealTests.Pipeline;

/// <summary>
/// Full round-trip tests: prompt → model triggers function call → we send mock result → model speaks.
/// Validates the complete function-call pipeline for M5 tools.
/// </summary>
[Trait("Category", "RealAPI")]
public class ToolRoundTripTests : IAsyncLifetime
{
    private readonly M5ToolFixture _fixture = new();
    private readonly ITestOutputHelper _output;

    public ToolRoundTripTests(ITestOutputHelper output)
    {
        _output = output;
        _fixture.SetOutput(output);
    }

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task ReadText_RoundTrip_ModelSpeaksText()
    {
        await _fixture.SendTextInputAsync(
            "Read the text on the sign in front of me. Use read_text.");

        await _fixture.WaitForFunctionCallAsync(TimeSpan.FromSeconds(30));

        if (!TryGetFunctionCall("read_text", out var call))
            return;

        _fixture.Reset();

        var mockResult = JsonSerializer.Serialize(new
        {
            text = "EXIT ONLY — Do Not Enter. Emergency exit. Push bar to open."
        });

        await _fixture.SendFunctionCallOutputAsync(call!.CallId, mockResult);
        await _fixture.WaitForResponseAsync(TimeSpan.FromSeconds(30));

        _fixture.OutputTranscripts.Should().NotBeEmpty("model should speak after receiving read result");
        _fixture.Errors.Should().BeEmpty();

        var spoken = _fixture.OutputTranscripts[0];
        _output.WriteLine($"Model said: {spoken}");

        var mentionsText = spoken.Contains("exit", StringComparison.OrdinalIgnoreCase)
                        || spoken.Contains("emergency", StringComparison.OrdinalIgnoreCase)
                        || spoken.Contains("push", StringComparison.OrdinalIgnoreCase);
        mentionsText.Should().BeTrue("model should relay the text that was read");
    }

    [Fact]
    public async Task SaveMemory_RoundTrip_ModelConfirmsSaved()
    {
        await _fixture.SendTextInputAsync(
            "Remember that my car is parked in spot B7 on level 2.");

        await _fixture.WaitForFunctionCallAsync(TimeSpan.FromSeconds(30));

        if (!TryGetFunctionCall("save_memory", out var call))
            return;

        _fixture.Reset();

        var mockResult = JsonSerializer.Serialize(new
        {
            saved = true,
            id = "abc123",
            content = "Car parked in spot B7 on level 2",
            category = "location"
        });

        await _fixture.SendFunctionCallOutputAsync(call!.CallId, mockResult);
        await _fixture.WaitForResponseAsync(TimeSpan.FromSeconds(30));

        _fixture.OutputTranscripts.Should().NotBeEmpty("model should confirm the save");
        _fixture.Errors.Should().BeEmpty();

        var spoken = _fixture.OutputTranscripts[0];
        _output.WriteLine($"Model said: {spoken}");

        var confirms = spoken.Contains("remember", StringComparison.OrdinalIgnoreCase)
                    || spoken.Contains("saved", StringComparison.OrdinalIgnoreCase)
                    || spoken.Contains("noted", StringComparison.OrdinalIgnoreCase)
                    || spoken.Contains("got it", StringComparison.OrdinalIgnoreCase)
                    || spoken.Contains("B7", StringComparison.OrdinalIgnoreCase);
        confirms.Should().BeTrue("model should confirm the memory was saved");
    }

    [Fact]
    public async Task FindObject_RoundTrip_ModelDescribesLocation()
    {
        await _fixture.SendTextInputAsync(
            "Find my red coffee mug. Look around for it.");

        await _fixture.WaitForFunctionCallAsync(TimeSpan.FromSeconds(30));

        if (!TryGetFunctionCall("find_object", out var call))
            return;

        _fixture.Reset();

        var mockResult = JsonSerializer.Serialize(new
        {
            found = true,
            description = "FOUND - The red coffee mug is on the left side of the desk, next to the monitor.",
            target = "red coffee mug"
        });

        await _fixture.SendFunctionCallOutputAsync(call!.CallId, mockResult);
        await _fixture.WaitForResponseAsync(TimeSpan.FromSeconds(30));

        _fixture.OutputTranscripts.Should().NotBeEmpty("model should describe where the object was found");
        _fixture.Errors.Should().BeEmpty();

        var spoken = _fixture.OutputTranscripts[0];
        _output.WriteLine($"Model said: {spoken}");

        var mentionsLocation = spoken.Contains("desk", StringComparison.OrdinalIgnoreCase)
                            || spoken.Contains("left", StringComparison.OrdinalIgnoreCase)
                            || spoken.Contains("monitor", StringComparison.OrdinalIgnoreCase)
                            || spoken.Contains("found", StringComparison.OrdinalIgnoreCase)
                            || spoken.Contains("mug", StringComparison.OrdinalIgnoreCase);
        mentionsLocation.Should().BeTrue("model should reference the object's location");
    }

    [Fact]
    public async Task NavigateTo_RoundTrip_ModelConfirmsNavigation()
    {
        await _fixture.SendTextInputAsync(
            "Start walking navigation to 350 Fifth Avenue, New York. Use navigate_to.");

        await _fixture.WaitForFunctionCallAsync(TimeSpan.FromSeconds(30));

        // Model may choose navigate_to or lookup_address — test whichever it picks
        var call = _fixture.FunctionCalls.FirstOrDefault(fc => fc.Name == "navigate_to")
                ?? _fixture.FunctionCalls.FirstOrDefault(fc => fc.Name == "lookup_address");

        if (call is null)
        {
            _output.WriteLine("No navigation-related function call triggered.");
            _output.WriteLine($"All calls: {string.Join(", ", _fixture.FunctionCalls.Select(fc => fc.Name))}");
            call.Should().NotBeNull("model should trigger navigate_to or lookup_address");
            return;
        }

        _output.WriteLine($"Function call: {call.Name} (callId: {call.CallId})");
        _output.WriteLine($"Arguments: {call.Arguments}");

        _fixture.Reset();

        var mockResult = JsonSerializer.Serialize(new
        {
            destination = "350 Fifth Avenue, New York",
            mode = "walking",
            status = "Navigation started"
        });

        await _fixture.SendFunctionCallOutputAsync(call.CallId, mockResult);
        await _fixture.WaitForResponseAsync(TimeSpan.FromSeconds(30));

        _fixture.OutputTranscripts.Should().NotBeEmpty("model should confirm navigation");
        _fixture.Errors.Should().BeEmpty();

        var spoken = _fixture.OutputTranscripts[0];
        _output.WriteLine($"Model said: {spoken}");
    }

    /// <summary>
    /// Helper: extracts a function call by name, or skips the test with diagnostic output.
    /// Returns false if no matching function call was found (test should return early).
    /// </summary>
    private bool TryGetFunctionCall(string toolName, out FunctionCallInfo? call)
    {
        call = _fixture.FunctionCalls.FirstOrDefault(fc => fc.Name == toolName);

        if (call is null)
        {
            _output.WriteLine($"No '{toolName}' function call triggered — model answered directly.");
            _output.WriteLine($"Transcripts: {string.Join("; ", _fixture.OutputTranscripts)}");
            _output.WriteLine($"All function calls: {string.Join(", ", _fixture.FunctionCalls.Select(fc => fc.Name))}");
            _output.WriteLine($"Event types: {string.Join(", ", _fixture.Events.Select(e => e.Type).Distinct())}");

            // Fail the test explicitly
            call.Should().NotBeNull($"model should trigger {toolName} function call");
            return false;
        }

        _output.WriteLine($"Function call: {call.Name} (callId: {call.CallId})");
        _output.WriteLine($"Arguments: {call.Arguments}");
        return true;
    }
}
