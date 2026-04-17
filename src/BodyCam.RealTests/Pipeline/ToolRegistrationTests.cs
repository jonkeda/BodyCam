using System.Text.Json;
using BodyCam.RealTests.Fixtures;
using BodyCam.Tools;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace BodyCam.RealTests.Pipeline;

/// <summary>
/// Tests that all 13 M5 tools register correctly with the Realtime API
/// and the session starts without errors.
/// </summary>
[Trait("Category", "RealAPI")]
public class ToolRegistrationTests : IAsyncLifetime
{
    private readonly M5ToolFixture _fixture = new();
    private readonly ITestOutputHelper _output;

    public ToolRegistrationTests(ITestOutputHelper output)
    {
        _output = output;
        _fixture.SetOutput(output);
    }

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public void AllThirteenToolsRegistered()
    {
        var defs = _fixture.Dispatcher.GetToolDefinitions();

        _output.WriteLine($"Registered tools ({defs.Count}):");
        foreach (var d in defs)
            _output.WriteLine($"  {d.Name}: {d.Description[..Math.Min(60, d.Description.Length)]}...");

        defs.Should().HaveCount(13);

        var names = defs.Select(d => d.Name).ToList();
        names.Should().Contain("describe_scene");
        names.Should().Contain("deep_analysis");
        names.Should().Contain("read_text");
        names.Should().Contain("take_photo");
        names.Should().Contain("save_memory");
        names.Should().Contain("recall_memory");
        names.Should().Contain("set_translation_mode");
        names.Should().Contain("make_phone_call");
        names.Should().Contain("send_message");
        names.Should().Contain("lookup_address");
        names.Should().Contain("find_object");
        names.Should().Contain("navigate_to");
        names.Should().Contain("start_scene_watch");
    }

    [Fact]
    public async Task SessionCreatedWithoutErrors()
    {
        // The fixture already connected in InitializeAsync.
        // Send a simple greeting to verify the session is alive.
        await _fixture.SendTextInputAsync("Hello.");

        await _fixture.WaitForResponseAsync(TimeSpan.FromSeconds(30));

        _fixture.Errors.Should().BeEmpty("session with 13 tools should start cleanly");
        _fixture.OutputTranscripts.Should().NotBeEmpty("model should respond to greeting");

        _output.WriteLine($"Model said: {_fixture.OutputTranscripts[0]}");
    }

    [Fact]
    public async Task ToolDefinitions_HaveValidSchemas()
    {
        var defs = _fixture.Dispatcher.GetToolDefinitions();

        foreach (var def in defs)
        {
            def.Name.Should().NotBeNullOrWhiteSpace();
            def.Description.Should().NotBeNullOrWhiteSpace();
            def.ParametersJson.Should().NotBeNullOrWhiteSpace();

            // Verify the schema is valid JSON
            var act = () => JsonDocument.Parse(def.ParametersJson);
            act.Should().NotThrow($"tool '{def.Name}' should have valid JSON schema");

            using var doc = JsonDocument.Parse(def.ParametersJson);
            doc.RootElement.GetProperty("type").GetString().Should().Be("object",
                $"tool '{def.Name}' schema root should be type=object");

            _output.WriteLine($"{def.Name}: schema OK ({def.ParametersJson.Length} chars)");
        }
    }
}
