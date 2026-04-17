# M1 Implementation — Step 5: Realtime Client WebSocket Implementation

**Depends on:** Step 1 (IApiKeyService), Step 2 (IRealtimeClient interface + models)
**Produces:** Working `RealtimeClient` that connects to OpenAI, sends/receives messages

---

## Why This Step?
This is the core of M1 — the actual WebSocket connection to OpenAI Realtime API. Once this works, we have the bridge between local audio and the AI model.

---

## Tasks

### 5.1 — Implement WebSocket message types

**File:** `src/BodyCam/Services/Realtime/RealtimeMessages.cs`

Define C# record types for the JSON messages sent/received over WebSocket. Use `System.Text.Json` source generation for performance.

**Client → Server messages:**
```csharp
namespace BodyCam.Services.Realtime;

// Base
internal record RealtimeMessage(string Type);

// session.update
internal record SessionUpdateMessage(
    string Type,
    SessionUpdatePayload Session
) : RealtimeMessage(Type);

internal record SessionUpdatePayload
{
    public string? Model { get; init; }
    public string[]? OutputModalities { get; init; } // ["audio"]
    public SessionAudioConfig? Audio { get; init; }
    public string? Instructions { get; init; }
    public InputAudioTranscription? InputAudioTranscription { get; init; }
}

internal record SessionAudioConfig
{
    public AudioInputConfig? Input { get; init; }
    public AudioOutputConfig? Output { get; init; }
}

internal record AudioInputConfig
{
    public AudioFormat? Format { get; init; }
    public TurnDetection? TurnDetection { get; init; }
    public NoiseReduction? NoiseReduction { get; init; }
}

internal record AudioOutputConfig
{
    public AudioFormat? Format { get; init; }
    public string? Voice { get; init; }
}

internal record AudioFormat(string Type, int? Rate = null);
internal record TurnDetection(string Type);
internal record NoiseReduction(string Type);
internal record InputAudioTranscription(string Model = "gpt-5.4-mini-transcribe");

// input_audio_buffer.append
internal record AudioBufferAppendMessage(
    string Type,
    string Audio          // base64-encoded PCM
) : RealtimeMessage(Type);

// input_audio_buffer.commit
// input_audio_buffer.clear
// response.create
// response.cancel
// — all just { "type": "..." }

// conversation.item.truncate
internal record TruncateMessage(
    string Type,
    string ItemId,
    int ContentIndex,
    int AudioEndMs
) : RealtimeMessage(Type);
```

**Server → Client parsing:**
Parse `type` field first, then deserialize to specific type. Use a switch on `type` string.

### 5.2 — Add JSON serialization context

**File:** `src/BodyCam/Services/Realtime/RealtimeJsonContext.cs`

```csharp
using System.Text.Json.Serialization;

namespace BodyCam.Services.Realtime;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SessionUpdateMessage))]
[JsonSerializable(typeof(AudioBufferAppendMessage))]
[JsonSerializable(typeof(TruncateMessage))]
[JsonSerializable(typeof(RealtimeMessage))]
internal partial class RealtimeJsonContext : JsonSerializerContext { }
```

### 5.3 — Implement `RealtimeClient`

**File:** `src/BodyCam/Services/RealtimeClient.cs` (replace stub from Step 2)

Key implementation details:
1. **ConnectAsync:** Open `ClientWebSocket` with `Authorization: Bearer {key}` header. URL: `wss://api.openai.com/v1/realtime?model={model}`
2. **Receive loop:** Background task reads WebSocket frames, parses JSON `type`, dispatches to events
3. **SendAudioChunkAsync:** Base64-encode PCM, send as `input_audio_buffer.append`
4. **UpdateSessionAsync:** Send `session.update` with config from `AppSettings`
5. **DisconnectAsync:** Close WebSocket gracefully
6. **DisposeAsync:** Cleanup

```csharp
namespace BodyCam.Services;

public class RealtimeClient : IRealtimeClient
{
    private readonly IApiKeyService _apiKeyService;
    private readonly AppSettings _settings;
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveLoop;

    // Constructor, IsConnected property...

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        var apiKey = await _apiKeyService.GetApiKeyAsync()
            ?? throw new InvalidOperationException("API key not set.");

        _ws = new ClientWebSocket();

        // Auth header differs by provider
        if (_settings.Provider == OpenAiProvider.Azure)
            _ws.Options.SetRequestHeader("api-key", apiKey);
        else
            _ws.Options.SetRequestHeader("Authorization", $"Bearer {apiKey}");

        // URI built by AppSettings.GetRealtimeUri() — handles both providers
        await _ws.ConnectAsync(_settings.GetRealtimeUri(), ct);

        _receiveCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _receiveLoop = Task.Run(() => ReceiveLoopAsync(_receiveCts.Token));

        IsConnected = true;
        await UpdateSessionAsync(ct);
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[64 * 1024]; // 64KB receive buffer
        var messageBuffer = new MemoryStream();

        while (!ct.IsCancellationRequested && _ws?.State == WebSocketState.Open)
        {
            var result = await _ws.ReceiveAsync(buffer, ct);
            messageBuffer.Write(buffer, 0, result.Count);

            if (result.EndOfMessage)
            {
                var json = Encoding.UTF8.GetString(messageBuffer.ToArray());
                messageBuffer.SetLength(0);
                DispatchMessage(json);
            }

            if (result.MessageType == WebSocketMessageType.Close)
                break;
        }
    }

    private void DispatchMessage(string json)
    {
        // Parse "type" field, switch → raise appropriate event
        // response.output_audio.delta → decode base64 → AudioDelta
        // response.output_audio_transcript.delta → OutputTranscriptDelta
        // conversation.item.input_audio_transcription.completed → InputTranscriptCompleted
        // input_audio_buffer.speech_started → SpeechStarted
        // input_audio_buffer.speech_stopped → SpeechStopped
        // response.done → ResponseDone
        // error → ErrorOccurred
    }

    public async Task SendAudioChunkAsync(byte[] pcm16Data, CancellationToken ct = default)
    {
        var msg = new AudioBufferAppendMessage(
            "input_audio_buffer.append",
            Convert.ToBase64String(pcm16Data));
        await SendJsonAsync(msg, ct);
    }

    // ... remaining methods
}
```

### 5.4 — Integration tests with WireMock

Update the existing `OpenAiWireMockFixture` to simulate a WebSocket endpoint (or create a new fixture for Realtime testing). Test scenarios:
- Connect → receive `session.created`
- Send `session.update` → receive `session.updated`
- Send `input_audio_buffer.append` → no error
- Simulate `response.output_audio.delta` → verify `AudioDelta` event fires
- Simulate `error` → verify `ErrorOccurred` event fires

**Note:** WireMock.Net has limited WebSocket support. Alternative: create a simple `TcpListener`-based mock WebSocket server for tests, or test at the message parsing level (unit test `DispatchMessage` with raw JSON strings).

### 5.5 — Unit tests for message serialization

Test that:
- `SessionUpdateMessage` serializes to correct JSON with snake_case
- `AudioBufferAppendMessage` serializes with base64 audio
- Server event JSON parses correctly and dispatches right events

---

## Verification

- [ ] `RealtimeClient` connects to OpenAI (manual test with real API key)
- [ ] `session.created` event received after connect
- [ ] Audio chunks sent without WebSocket errors
- [ ] Server events parsed and dispatched to correct event handlers
- [ ] Disconnect/reconnect works cleanly
- [ ] API key never logged (verify no `Console.Write` or `Debug.Write` of key)
- [ ] All existing + new tests pass

---

## Files Changed

| File | Action |
|------|--------|
| `Services/Realtime/RealtimeMessages.cs` | NEW |
| `Services/Realtime/RealtimeJsonContext.cs` | NEW |
| `Services/RealtimeClient.cs` | REPLACE stub with real implementation |
| `Tests/Services/RealtimeMessageSerializationTests.cs` | NEW |
| `IntegrationTests/Services/RealtimeClientTests.cs` | NEW (or modify existing) |
