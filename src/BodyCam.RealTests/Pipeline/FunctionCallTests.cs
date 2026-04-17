using System.Text.Json;
using BodyCam.RealTests.Fixtures;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace BodyCam.RealTests.Pipeline;

/// <summary>
/// Tests that trigger function calls via text prompts and verify the round-trip.
/// </summary>
[Trait("Category", "RealAPI")]
public class FunctionCallTests : IAsyncLifetime
{
    private readonly RealtimeFixture _fixture = new();
    private readonly ITestOutputHelper _output;

    public FunctionCallTests(ITestOutputHelper output)
    {
        _output = output;
        _fixture.SetOutput(output);
    }

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task AskAboutSurroundings_TriggersFunctionCall()
    {
        await _fixture.SendTextInputAsync("What do you see around me right now? Describe what the camera shows.");

        // The model should invoke describe_scene
        await _fixture.WaitForResponseAsync(TimeSpan.FromSeconds(30));

        // If no function call, the model answered directly — log it for diagnosis
        if (_fixture.FunctionCalls.Count == 0)
        {
            _output.WriteLine("No function call was triggered. Model response:");
            foreach (var t in _fixture.OutputTranscripts)
                _output.WriteLine($"  {t}");

            // Log all events for debugging
            _output.WriteLine("All events:");
            foreach (var (type, _) in _fixture.Events)
                _output.WriteLine($"  {type}");
        }

        _fixture.FunctionCalls.Should().HaveCountGreaterThanOrEqualTo(1,
            "asking about surroundings should trigger describe_scene");
        _fixture.FunctionCalls[0].Name.Should().Be("describe_scene");
    }

    [Fact]
    public async Task FunctionCallOutput_CompletesRoundTrip()
    {
        await _fixture.SendTextInputAsync("Look at what's in front of me and describe it.");

        // Wait for the function call (response.done fires after function call is parsed)
        await _fixture.WaitForResponseAsync(TimeSpan.FromSeconds(30));

        if (_fixture.FunctionCalls.Count == 0)
        {
            _output.WriteLine("No function call triggered — skipping round-trip test.");
            _output.WriteLine("Model said: " + string.Join("; ", _fixture.OutputTranscripts));
            return;
        }

        var call = _fixture.FunctionCalls[0];
        call.Name.Should().Be("describe_scene");
        call.CallId.Should().NotBeNullOrEmpty();

        _output.WriteLine($"Function call received: {call.Name} (callId: {call.CallId})");

        // Reset to track the continuation response
        _fixture.Reset();

        // Send the function result back
        var mockDescription = JsonSerializer.Serialize(new
        {
            description = "A red coffee mug on a wooden desk next to a laptop."
        });
        await _fixture.SendFunctionCallOutputAsync(call.CallId, mockDescription);

        // Wait for the model to speak the result
        await _fixture.WaitForResponseAsync(TimeSpan.FromSeconds(30));

        _fixture.OutputTranscripts.Should().NotBeEmpty(
            "model should speak after receiving function call output");
        _fixture.AudioChunks.Should().NotBeEmpty(
            "model should produce audio after function call output");
        _fixture.Errors.Should().BeEmpty();

        _output.WriteLine($"Model said: {_fixture.OutputTranscripts[0]}");
    }

    [Fact]
    public async Task DeepAnalysis_TriggersFunctionCall()
    {
        await _fixture.SendTextInputAsync(
            "I need a really thorough and deep analysis of why the sky appears blue. Use your deep analysis capability.");

        await _fixture.WaitForResponseAsync(TimeSpan.FromSeconds(30));

        if (_fixture.FunctionCalls.Count == 0)
        {
            _output.WriteLine("No function call triggered for deep_analysis.");
            _output.WriteLine("Model said: " + string.Join("; ", _fixture.OutputTranscripts));

            // Log event types
            _output.WriteLine("Event types:");
            foreach (var type in _fixture.Events.Select(e => e.Type).Distinct())
                _output.WriteLine($"  {type}");
        }

        _fixture.FunctionCalls.Should().HaveCountGreaterThanOrEqualTo(1,
            "asking for deep analysis should trigger deep_analysis function");
        _fixture.FunctionCalls.Should().Contain(fc => fc.Name == "deep_analysis",
            "should trigger deep_analysis specifically");
    }
}
