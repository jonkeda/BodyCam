using System.Text.Json;
using BodyCam.Agents;
using BodyCam.Services;
using BodyCam.Tools;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Xunit;
using Xunit.Abstractions;

using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace BodyCam.RealTests.Pipeline;

/// <summary>
/// Validates that all tools register correctly with valid schemas.
/// No API connection needed — these are pure validation tests.
/// </summary>
public class ToolRegistrationTests
{
    private readonly ToolDispatcher _dispatcher;
    private readonly ITestOutputHelper _output;

    public ToolRegistrationTests(ITestOutputHelper output)
    {
        _output = output;

        // Stub chat client — tools are only instantiated for their metadata, not executed
        var stubChat = new StubChatClient();
        var settings = new AppSettings();
        var vision = new VisionAgent(stubChat, settings);
        var conversation = new ConversationAgent(stubChat, settings);
        var memoryStore = new MemoryStore(Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.json"));

        var tools = new ITool[]
        {
            new DescribeSceneTool(vision),
            new DeepAnalysisTool(conversation),
            new ReadTextTool(vision),
            new TakePhotoTool(),
            new SaveMemoryTool(memoryStore),
            new RecallMemoryTool(memoryStore),
            new SetTranslationModeTool(),
            new MakePhoneCallTool(),
            new SendMessageTool(),
            new LookupAddressTool(),
            new FindObjectTool(vision),
            new NavigateToTool(),
            new StartSceneWatchTool(vision),
        };

        _dispatcher = new ToolDispatcher(tools);
    }

    [Fact]
    public void AllThirteenToolsRegistered()
    {
        var defs = _dispatcher.GetToolDefinitions();

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
    public void ToolDefinitions_HaveValidSchemas()
    {
        var defs = _dispatcher.GetToolDefinitions();

        foreach (var def in defs)
        {
            def.Name.Should().NotBeNullOrWhiteSpace();
            def.Description.Should().NotBeNullOrWhiteSpace();
            def.ParametersJson.Should().NotBeNullOrWhiteSpace();

            var act = () => JsonDocument.Parse(def.ParametersJson);
            act.Should().NotThrow($"tool '{def.Name}' should have valid JSON schema");

            using var doc = JsonDocument.Parse(def.ParametersJson);
            doc.RootElement.GetProperty("type").GetString().Should().Be("object",
                $"tool '{def.Name}' schema root should be type=object");

            _output.WriteLine($"{def.Name}: schema OK ({def.ParametersJson.Length} chars)");
        }
    }

    private sealed class StubChatClient : IChatClient
    {
        public ChatClientMetadata Metadata => new("stub");

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<AIChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new AIChatMessage(ChatRole.Assistant, "stub")));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<AIChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => AsyncEnumerable.Empty<ChatResponseUpdate>();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
