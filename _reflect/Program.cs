using System.Reflection;
using Microsoft.Extensions.AI;

var types = new[] { 
    typeof(RealtimeSessionOptions),
    typeof(InputAudioBufferAppendRealtimeClientMessage),
    typeof(CreateConversationItemRealtimeClientMessage),
    typeof(CreateResponseRealtimeClientMessage),
    typeof(RealtimeConversationItem)
};

foreach (var t in types) {
    Console.WriteLine($"=== {t.Name} ===");
    foreach (var c in t.GetConstructors()) {
        var ps = c.GetParameters();
        Console.WriteLine($"  ctor({string.Join(", ", ps.Select(p => $"{p.ParameterType.Name} {p.Name}{(p.HasDefaultValue ? $"={p.DefaultValue}" : "")}"))})");
    }
    foreach (var p in t.GetProperties()) {
        Console.WriteLine($"  {p.PropertyType.Name} {p.Name} {(p.CanWrite?"get/set":"get")}");
    }
}

// Also check TranscriptionOptions property type
var transcOpt = typeof(RealtimeSessionOptions).GetProperty("TranscriptionOptions");
if (transcOpt != null) {
    Console.WriteLine($"\n=== TranscriptionOptions type: {transcOpt.PropertyType.FullName} ===");
    foreach (var p in transcOpt.PropertyType.GetProperties()) {
        Console.WriteLine($"  {p.PropertyType.Name} {p.Name} {(p.CanWrite?"get/set":"get")}");
    }
}
