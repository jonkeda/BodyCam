using System.Text.Json.Serialization;

namespace BodyCam.Services.Realtime;

[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
[JsonSerializable(typeof(SessionUpdateMessage))]
[JsonSerializable(typeof(AudioBufferAppendMessage))]
[JsonSerializable(typeof(TruncateMessage))]
[JsonSerializable(typeof(ConversationItemCreateMessage))]
[JsonSerializable(typeof(FunctionCallOutputMessage))]
[JsonSerializable(typeof(RealtimeMessage))]
internal partial class RealtimeJsonContext : JsonSerializerContext { }
