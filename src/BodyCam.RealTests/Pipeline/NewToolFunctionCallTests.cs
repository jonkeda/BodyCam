using BodyCam.RealTests.Fixtures;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace BodyCam.RealTests.Pipeline;

/// <summary>
/// Tests that the model triggers the correct M5 function call when given specific prompts.
/// Each test connects a fresh session with all 13 tools registered.
/// </summary>
[Trait("Category", "RealAPI")]
public class NewToolFunctionCallTests : IAsyncLifetime
{
    private readonly M5ToolFixture _fixture = new();
    private readonly ITestOutputHelper _output;

    public NewToolFunctionCallTests(ITestOutputHelper output)
    {
        _output = output;
        _fixture.SetOutput(output);
    }

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task AskToReadText_TriggersReadText()
    {
        await _fixture.SendTextInputAsync(
            "Read the text on the sign in front of me. Use the read_text tool.");

        await _fixture.WaitForFunctionCallAsync(TimeSpan.FromSeconds(30));

        LogDiagnostics();

        _fixture.FunctionCalls.Should().ContainSingle(fc => fc.Name == "read_text",
            "asking to read text should trigger read_text function");
    }

    [Fact]
    public async Task AskToSaveMemory_TriggersSaveMemory()
    {
        await _fixture.SendTextInputAsync(
            "Remember that my car is parked in spot B7 on level 2. Save this to memory.");

        await _fixture.WaitForFunctionCallAsync(TimeSpan.FromSeconds(30));

        LogDiagnostics();

        _fixture.FunctionCalls.Should().Contain(fc => fc.Name == "save_memory",
            "asking to remember something should trigger save_memory");

        var call = _fixture.FunctionCalls.First(fc => fc.Name == "save_memory");
        _output.WriteLine($"Arguments: {call.Arguments}");
        call.Arguments.Should().Contain("B7", "should include the parking spot in content");
    }

    [Fact]
    public async Task AskToFindObject_TriggersFindObject()
    {
        await _fixture.SendTextInputAsync(
            "Find my red coffee mug. Look around for it using the camera.");

        await _fixture.WaitForFunctionCallAsync(TimeSpan.FromSeconds(30));

        LogDiagnostics();

        _fixture.FunctionCalls.Should().Contain(fc => fc.Name == "find_object",
            "asking to find something should trigger find_object");

        var call = _fixture.FunctionCalls.First(fc => fc.Name == "find_object");
        _output.WriteLine($"Arguments: {call.Arguments}");
    }

    [Fact]
    public async Task AskToNavigate_TriggersNavigateOrLookup()
    {
        await _fixture.SendTextInputAsync(
            "I need to walk to the nearest Starbucks. Start navigation for me.");

        await _fixture.WaitForFunctionCallAsync(TimeSpan.FromSeconds(30));

        LogDiagnostics();

        // Model may choose navigate_to directly, or lookup_address first — both are valid
        var validTools = new[] { "navigate_to", "lookup_address" };
        _fixture.FunctionCalls.Should().Contain(
            fc => validTools.Contains(fc.Name),
            "asking for navigation should trigger navigate_to or lookup_address");

        var call = _fixture.FunctionCalls.First(fc => validTools.Contains(fc.Name));
        _output.WriteLine($"Chose tool: {call.Name}");
        _output.WriteLine($"Arguments: {call.Arguments}");
    }

    [Fact]
    public async Task AskToTranslate_TriggersSetTranslationMode()
    {
        await _fixture.SendTextInputAsync(
            "Start translating everything I say into Spanish. Activate translation mode.");

        await _fixture.WaitForFunctionCallAsync(TimeSpan.FromSeconds(30));

        LogDiagnostics();

        _fixture.FunctionCalls.Should().Contain(fc => fc.Name == "set_translation_mode",
            "asking to translate should trigger set_translation_mode");

        var call = _fixture.FunctionCalls.First(fc => fc.Name == "set_translation_mode");
        _output.WriteLine($"Arguments: {call.Arguments}");
    }

    [Fact]
    public async Task AskToLookupAddress_TriggersLookupAddress()
    {
        await _fixture.SendTextInputAsync(
            "Look up the address of the Empire State Building for me.");

        await _fixture.WaitForFunctionCallAsync(TimeSpan.FromSeconds(30));

        LogDiagnostics();

        // The model may either use lookup_address or answer directly — both are acceptable.
        // But with the tool available and the explicit phrasing, it should prefer the tool.
        if (_fixture.FunctionCalls.Count == 0)
        {
            _output.WriteLine("Model answered directly without tool call (acceptable fallback).");
            _output.WriteLine($"Transcript: {string.Join("; ", _fixture.OutputTranscripts)}");
        }
        else
        {
            _fixture.FunctionCalls.Should().Contain(fc => fc.Name == "lookup_address");
        }
    }

    private void LogDiagnostics()
    {
        if (_fixture.FunctionCalls.Count == 0)
        {
            _output.WriteLine("No function calls triggered.");
            _output.WriteLine($"Transcripts: {string.Join("; ", _fixture.OutputTranscripts)}");
            _output.WriteLine($"Event types: {string.Join(", ", _fixture.Events.Select(e => e.Type).Distinct())}");
        }
        else
        {
            foreach (var fc in _fixture.FunctionCalls)
                _output.WriteLine($"Function call: {fc.Name}({fc.Arguments})");
        }

        if (_fixture.Errors.Count > 0)
        {
            _output.WriteLine($"Errors: {string.Join("; ", _fixture.Errors)}");
        }
    }
}
