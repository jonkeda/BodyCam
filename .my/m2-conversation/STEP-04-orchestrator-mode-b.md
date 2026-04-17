# M2 Implementation — Step 4: Orchestrator Mode B Flow

**Depends on:** Step 3 (ConversationAgent with streaming Chat Completions)
**Produces:** Dual-mode `AgentOrchestrator` — Mode A unchanged, Mode B routes transcript through ConversationAgent to TTS

---

## Why This Step?

This is the core M2 wiring. The orchestrator must:
- **Mode A (Realtime):** Keep current behavior exactly. Audio in → Realtime API → audio out + transcripts.
- **Mode B (Separated):** Audio in → Realtime API (STT only) → transcript → ConversationAgent → reply text → TTS → audio out.

---

## Tasks

### 4.1 — Add Realtime API "text-only" session config for Mode B

**File:** `src/BodyCam/Services/RealtimeClient.cs` — MODIFY

In `UpdateSessionAsync`, when Mode B is active, configure the Realtime session to **not generate audio output** — it only does STT:

```csharp
public async Task UpdateSessionAsync(CancellationToken ct = default)
{
    var modalities = _settings.Mode == ConversationMode.Separated
        ? new[] { "text" }           // STT only — no audio output from Realtime
        : new[] { "text", "audio" }; // Full audio in+out (Mode A)

    var msg = new SessionUpdateMessage
    {
        Type = "session.update",
        Session = new SessionUpdatePayload
        {
            Modalities = modalities,
            Voice = _settings.Voice,
            Instructions = _settings.SystemInstructions,
            InputAudioFormat = "pcm16",
            OutputAudioFormat = "pcm16",
            InputAudioTranscription = new InputAudioTranscription { Model = _settings.TranscriptionModel },
            TurnDetection = new TurnDetectionConfig { Type = _settings.TurnDetection }
        }
    };
    await SendJsonAsync(msg, RealtimeJsonContext.Default.SessionUpdateMessage, ct);
}
```

### 4.2 — Add TTS-via-Realtime method

**File:** `src/BodyCam/Services/IRealtimeClient.cs` — MODIFY

Add a method to inject text and request audio generation:

```csharp
/// <summary>
/// Mode B: Send reply text to Realtime API to generate TTS audio.
/// Creates a conversation item with the text, then triggers response with audio.
/// </summary>
Task SendTextForTtsAsync(string text, CancellationToken ct = default);
```

**File:** `src/BodyCam/Services/RealtimeClient.cs` — MODIFY

```csharp
public async Task SendTextForTtsAsync(string text, CancellationToken ct = default)
{
    // 1. Create a conversation item with the assistant's text
    var itemMsg = new ConversationItemCreateMessage
    {
        Type = "conversation.item.create",
        Item = new ConversationItem
        {
            Type = "message",
            Role = "assistant",
            Content = [new ContentPart { Type = "input_text", Text = text }]
        }
    };
    await SendJsonAsync(itemMsg, RealtimeJsonContext.Default.ConversationItemCreateMessage, ct);

    // 2. Request a response — the Realtime API will generate audio from the text
    await CreateResponseAsync(ct);
}
```

**File:** `src/BodyCam/Services/Realtime/RealtimeMessages.cs` — MODIFY

Add the new message types:

```csharp
public class ConversationItemCreateMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "conversation.item.create";

    [JsonPropertyName("item")]
    public ConversationItem Item { get; set; } = new();
}

public class ConversationItem
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "message";

    [JsonPropertyName("role")]
    public string Role { get; set; } = "assistant";

    [JsonPropertyName("content")]
    public ContentPart[] Content { get; set; } = [];
}

public class ContentPart
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "input_text";

    [JsonPropertyName("text")]
    public string? Text { get; set; }
}
```

Update `RealtimeJsonContext` with the new types.

### 4.3 — Rewrite `AgentOrchestrator` for dual-mode

**File:** `src/BodyCam/Orchestration/AgentOrchestrator.cs` — REWRITE

The key change: in Mode B, `OnInputTranscriptCompleted` triggers the separated pipeline instead of just logging.

```csharp
public class AgentOrchestrator
{
    // ... existing fields ...
    private CancellationTokenSource? _turnCts; // Per-turn cancellation for Mode B

    // New event for Mode B streaming
    public event EventHandler<string>? ConversationReplyDelta;
    public event EventHandler<string>? ConversationReplyCompleted;

    // ... existing constructor, StartAsync, StopAsync ...

    // Mode B: when user transcript arrives, route through ConversationAgent
    private async void OnInputTranscriptCompleted(object? sender, string transcript)
    {
        _conversation.AddUserMessage(transcript, Session);
        TranscriptUpdated?.Invoke(this, $"You: {transcript}");
        TranscriptCompleted?.Invoke(this, $"You:{transcript}");
        DebugLog?.Invoke(this, $"User said: {transcript}");

        if (_settings.Mode == ConversationMode.Separated)
        {
            await ProcessModeBAsync(transcript);
        }
        // Mode A: Realtime API will generate the response automatically
    }

    private async Task ProcessModeBAsync(string transcript)
    {
        // Cancel any in-flight previous turn
        _turnCts?.Cancel();
        _turnCts?.Dispose();
        _turnCts = new CancellationTokenSource();
        var ct = _turnCts.Token;

        try
        {
            DebugLog?.Invoke(this, "Mode B: Processing via ConversationAgent...");

            var replyBuilder = new System.Text.StringBuilder();

            await foreach (var token in _conversation.ProcessTranscriptAsync(
                transcript, Session, ct))
            {
                replyBuilder.Append(token);
                ConversationReplyDelta?.Invoke(this, token);
                TranscriptDelta?.Invoke(this, token);
            }

            var fullReply = replyBuilder.ToString();
            if (fullReply.Length > 0)
            {
                ConversationReplyCompleted?.Invoke(this, fullReply);
                TranscriptCompleted?.Invoke(this, $"AI:{fullReply}");
                DebugLog?.Invoke(this, $"AI replied: {fullReply}");

                // Send reply to Realtime API for TTS
                if (_realtime.IsConnected && !ct.IsCancellationRequested)
                {
                    await _realtime.SendTextForTtsAsync(fullReply, ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
            DebugLog?.Invoke(this, "Mode B: Turn cancelled (interruption).");
        }
        catch (Exception ex)
        {
            DebugLog?.Invoke(this, $"Mode B error: {ex.Message}");
        }
    }

    // Mode A handlers remain unchanged:
    private void OnOutputTranscriptDelta(object? sender, string delta) { /* same as M1 */ }
    private void OnOutputTranscriptCompleted(object? sender, string transcript) { /* same as M1 */ }

    // ... rest unchanged ...
}
```

### 4.4 — Handle Mode B event wiring in StartAsync

In `StartAsync`, subscribe to Realtime events. In Mode B, the `AudioDelta`, `OutputTranscriptDelta`, and `OutputTranscriptCompleted` events still fire (from TTS playback of the ConversationAgent's reply). The flow is:

1. **User speaks** → Realtime fires `InputTranscriptCompleted` → orchestrator calls `ProcessModeBAsync`
2. **ConversationAgent streams** → orchestrator fires `TranscriptDelta` for UI
3. **Reply complete** → orchestrator calls `SendTextForTtsAsync` → Realtime generates audio
4. **Realtime fires `AudioDelta`** → `VoiceOutputAgent` plays audio (same as Mode A)

In Mode B, `OutputTranscriptDelta`/`OutputTranscriptCompleted` from the TTS pass are **ignored** (the orchestrator already showed the ConversationAgent's text). Add a guard:

```csharp
private void OnOutputTranscriptCompleted(object? sender, string transcript)
{
    if (_settings.Mode == ConversationMode.Separated)
        return; // Already handled by ConversationAgent

    _conversation.AddAssistantMessage(transcript, Session);
    TranscriptCompleted?.Invoke(this, $"AI:{transcript}");
    DebugLog?.Invoke(this, $"AI said: {transcript}");
}

private void OnOutputTranscriptDelta(object? sender, string delta)
{
    if (_settings.Mode == ConversationMode.Separated)
        return; // Already handled by ConversationAgent streaming

    TranscriptUpdated?.Invoke(this, $"AI: {delta}");
    TranscriptDelta?.Invoke(this, delta);
}
```

---

## Verification

- [ ] Mode A works exactly as before (no regression)
- [ ] Mode B: speak → see transcript → see AI reply stream in debug log → hear TTS audio
- [ ] Mode B: ConversationAgent adds both user and assistant messages to SessionContext
- [ ] Mode B: Realtime API is configured with `["text"]` modalities only
- [ ] Mode B: TTS audio plays through speakers after Chat Completions reply
- [ ] Switching modes in settings takes effect on next Start

---

## Risks

| Risk | Mitigation |
|------|-----------|
| Realtime API rejects text-only modality | Test with `["text"]` first; fallback: keep `["text","audio"]` and ignore generated audio in Mode B |
| TTS-via-Realtime latency | The user sees streaming text immediately; audio follows. Acceptable UX. |
| Race between ConversationAgent reply and Realtime TTS events | Mode B ignores `OutputTranscript*` events from Realtime |
| `conversation.item.create` API format changes | Pin to documented format, test early |
