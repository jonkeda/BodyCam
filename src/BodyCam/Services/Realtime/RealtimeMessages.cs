using System.Text.Json;
using System.Text.Json.Serialization;

namespace BodyCam.Services.Realtime;

// --- Client → Server messages ---

internal record RealtimeMessage
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "";
}

internal record SessionUpdateMessage : RealtimeMessage
{
    [JsonPropertyName("session")]
    public SessionUpdatePayload? Session { get; init; }
}

internal record SessionUpdatePayload
{
    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("modalities")]
    public string[]? Modalities { get; init; }

    [JsonPropertyName("voice")]
    public string? Voice { get; init; }

    [JsonPropertyName("instructions")]
    public string? Instructions { get; init; }

    [JsonPropertyName("input_audio_format")]
    public string? InputAudioFormat { get; init; }

    [JsonPropertyName("output_audio_format")]
    public string? OutputAudioFormat { get; init; }

    [JsonPropertyName("input_audio_transcription")]
    public InputAudioTranscription? InputAudioTranscription { get; init; }

    [JsonPropertyName("turn_detection")]
    public TurnDetectionConfig? TurnDetection { get; init; }

    [JsonPropertyName("tools")]
    public ToolDefinition[]? Tools { get; init; }

    [JsonPropertyName("tool_choice")]
    public string? ToolChoice { get; init; }
}

internal record InputAudioTranscription
{
    [JsonPropertyName("model")]
    public string Model { get; init; } = "gpt-4o-mini-transcribe";
}

internal record TurnDetectionConfig
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "semantic_vad";

    [JsonPropertyName("eagerness")]
    public string? Eagerness { get; init; }
}

internal record AudioBufferAppendMessage : RealtimeMessage
{
    [JsonPropertyName("audio")]
    public string Audio { get; init; } = "";
}

internal record TruncateMessage : RealtimeMessage
{
    [JsonPropertyName("item_id")]
    public string ItemId { get; init; } = "";

    [JsonPropertyName("content_index")]
    public int ContentIndex { get; init; }

    [JsonPropertyName("audio_end_ms")]
    public int AudioEndMs { get; init; }
}

internal record ConversationItemCreateMessage : RealtimeMessage
{
    [JsonPropertyName("item")]
    public ConversationItem Item { get; init; } = new();
}

internal record ConversationItem
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "message";

    [JsonPropertyName("role")]
    public string Role { get; init; } = "assistant";

    [JsonPropertyName("content")]
    public ContentPart[] Content { get; init; } = [];
}

internal record ContentPart
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "input_text";

    [JsonPropertyName("text")]
    public string? Text { get; init; }
}

// --- Tool definitions for session.update ---

internal record ToolDefinition
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "function";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("description")]
    public string Description { get; init; } = "";

    [JsonPropertyName("parameters")]
    public JsonElement Parameters { get; init; }
}

// --- Function call output (client → server) ---

internal record FunctionCallOutputMessage : RealtimeMessage
{
    [JsonPropertyName("item")]
    public FunctionCallOutputItem Item { get; init; } = new();
}

internal record FunctionCallOutputItem
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "function_call_output";

    [JsonPropertyName("call_id")]
    public string CallId { get; init; } = "";

    [JsonPropertyName("output")]
    public string Output { get; init; } = "";
}

// --- Server → Client event parsing ---

internal static class ServerEventParser
{
    public static string? GetType(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("type", out var typeProp))
                return typeProp.GetString();
        }
        catch { }
        return null;
    }

    public static string? GetStringProperty(string json, string propertyName)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(propertyName, out var prop))
                return prop.GetString();
        }
        catch { }
        return null;
    }

    public static string? GetNestedStringProperty(string json, string parent, string child)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(parent, out var parentProp) &&
                parentProp.TryGetProperty(child, out var childProp))
                return childProp.GetString();
        }
        catch { }
        return null;
    }

    public static (string? responseId, string? itemId, string? outputTranscript, string? inputTranscript) ParseResponseDone(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string? responseId = null;
            string? itemId = null;
            string? outputTranscript = null;

            if (root.TryGetProperty("response", out var resp))
            {
                if (resp.TryGetProperty("id", out var id))
                    responseId = id.GetString();

                if (resp.TryGetProperty("output", out var output) && output.GetArrayLength() > 0)
                {
                    var firstOutput = output[0];
                    if (firstOutput.TryGetProperty("id", out var oid))
                        itemId = oid.GetString();
                }
            }

            return (responseId, itemId, outputTranscript, null);
        }
        catch { return (null, null, null, null); }
    }

    public static List<(string callId, string name, string arguments)> ParseFunctionCalls(string json)
    {
        var results = new List<(string, string, string)>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("response", out var resp) &&
                resp.TryGetProperty("output", out var output))
            {
                foreach (var item in output.EnumerateArray())
                {
                    if (item.TryGetProperty("type", out var typeProp) &&
                        typeProp.GetString() == "function_call" &&
                        item.TryGetProperty("call_id", out var callIdProp) &&
                        item.TryGetProperty("name", out var nameProp) &&
                        item.TryGetProperty("arguments", out var argsProp))
                    {
                        results.Add((
                            callIdProp.GetString() ?? "",
                            nameProp.GetString() ?? "",
                            argsProp.GetString() ?? ""
                        ));
                    }
                }
            }
        }
        catch { }
        return results;
    }
}
