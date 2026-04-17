# Step 1: Remove Mode B Infrastructure

Remove the Separated pipeline, ConversationMode enum, and all dual-path code. After this step, only the Realtime (Mode A) path remains.

## Files Modified

### 1. `src/BodyCam/AppSettings.cs`

**Remove** `ConversationMode` enum entirely (lines 5–12):
```csharp
// DELETE:
public enum ConversationMode
{
    /// <summary>Realtime API handles reasoning + TTS (M1 behavior).</summary>
    Realtime,
    /// <summary>Separated pipeline: Realtime STT → ConversationAgent → TTS.</summary>
    Separated
}
```

**Remove** the `Mode` property from `AppSettings`:
```csharp
// DELETE:
public ConversationMode Mode { get; set; } = ConversationMode.Realtime;
```

### 2. `src/BodyCam/ModelOptions.cs`

**Remove** the `ConversationModes` array:
```csharp
// DELETE:
public static readonly string[] ConversationModes = ["Realtime", "Separated"];
```

### 3. `src/BodyCam/Services/IRealtimeClient.cs`

**Remove** `SendTextForTtsAsync`:
```csharp
// DELETE:
/// <summary>
/// Mode B: Send reply text to Realtime API to generate TTS audio.
/// Creates a conversation item with the text, then triggers response with audio.
/// </summary>
Task SendTextForTtsAsync(string text, CancellationToken ct = default);
```

### 4. `src/BodyCam/Services/RealtimeClient.cs`

**Remove** `SendTextForTtsAsync` method (entire method body ~20 lines).

**Simplify** `UpdateSessionAsync` — remove Mode check, always use `["text", "audio"]`:
```csharp
// BEFORE:
var modalities = _settings.Mode == ConversationMode.Separated
    ? new[] { "text" }
    : new[] { "text", "audio" };

// AFTER:
var modalities = new[] { "text", "audio" };
```

### 5. `src/BodyCam/Services/Realtime/RealtimeMessages.cs`

**Remove** `ResponseCreateMessage` and `ResponseCreatePayload` (Mode B TTS override types):
```csharp
// DELETE:
internal record ResponseCreateMessage : RealtimeMessage
{
    [JsonPropertyName("response")]
    public ResponseCreatePayload? Response { get; init; }
}

internal record ResponseCreatePayload
{
    [JsonPropertyName("modalities")]
    public string[]? Modalities { get; init; }
}
```

**Keep** `ConversationItemCreateMessage` and `ConversationItem` — needed for function_call_output in Step 2.

### 6. `src/BodyCam/Services/Realtime/RealtimeJsonContext.cs`

**Remove** `ResponseCreateMessage` from JSON context:
```csharp
// DELETE this line:
[JsonSerializable(typeof(ResponseCreateMessage))]
```

### 7. `src/BodyCam/Orchestration/AgentOrchestrator.cs`

**Remove** events:
```csharp
// DELETE:
public event EventHandler<string>? ConversationReplyDelta;
public event EventHandler<string>? ConversationReplyCompleted;
```

**Remove** `_turnCts` field and all references.

**Remove** Mode B branch in `OnInputTranscriptCompleted`:
```csharp
// BEFORE:
if (_settings.Mode == ConversationMode.Separated)
{
    await ProcessModeBAsync(transcript);
}
else
{
    _conversation.AddUserMessage(transcript, Session);
}

// AFTER:
_conversation.AddUserMessage(transcript, Session);
```

**Remove** entire `ProcessModeBAsync` method.

**Remove** Mode B guard in `OnOutputTranscriptDelta`:
```csharp
// DELETE:
if (_settings.Mode == ConversationMode.Separated)
    return;
```

**Remove** Mode B guard in `OnOutputTranscriptCompleted`:
```csharp
// DELETE:
if (_settings.Mode == ConversationMode.Separated)
    return;
```

**Simplify** `OnSpeechStarted` — remove entire Mode B branch:
```csharp
// DELETE the Mode B block:
if (_settings.Mode == ConversationMode.Separated)
{
    _turnCts?.Cancel();
    _voiceOut.HandleInterruption();
    _voiceOut.ResetTracker();
    try { await _realtime.CancelResponseAsync(); }
    catch (Exception ex) { ... }
    DebugLog?.Invoke(this, "Mode B: Interrupted ...");
    return;
}
```

**Remove** `_settings.Mode = _settingsService.Mode;` from `StartAsync`.

**Remove** `_turnCts?.Cancel(); _turnCts?.Dispose(); _turnCts = null;` from `StopAsync`.

### 8. `src/BodyCam/ViewModels/MainViewModel.cs`

**Remove** `ModeLabel` property:
```csharp
// DELETE:
public string ModeLabel => _settingsService.Mode == ConversationMode.Separated
    ? "[Mode B]"
    : "[Realtime]";
```

**Remove** `OnPropertyChanged(nameof(ModeLabel))` call from `ToggleAsync`.

**Remove** Mode B status transitions:
```csharp
// DELETE this block from TranscriptCompleted handler:
if (_settingsService.Mode == ConversationMode.Separated)
    StatusText = "Thinking...";

// DELETE entire ConversationReplyDelta subscription:
_orchestrator.ConversationReplyDelta += (_, _) => { ... };

// DELETE entire ConversationReplyCompleted subscription:
_orchestrator.ConversationReplyCompleted += (_, _) => { ... };
```

### 9. `src/BodyCam/ViewModels/SettingsViewModel.cs`

**Remove** `ConversationModeOptions` property:
```csharp
// DELETE:
public string[] ConversationModeOptions => ModelOptions.ConversationModes;
```

**Remove** `SelectedMode` property:
```csharp
// DELETE:
public string SelectedMode
{
    get => _settings.Mode.ToString();
    set
    {
        if (Enum.TryParse<ConversationMode>(value, true, out var mode))
            SetProperty(_settings.Mode.ToString(), value, _ => _settings.Mode = mode);
    }
}
```

### 10. `src/BodyCam/SettingsPage.xaml`

**Remove** the Conversation Mode picker:
```xml
<!-- DELETE: -->
<Label Text="Conversation Mode" FontSize="13" TextColor="Gray" />
<Picker ItemsSource="{Binding ConversationModeOptions}"
        SelectedItem="{Binding SelectedMode}" />
```

### 11. `src/BodyCam/MainPage.xaml`

**Remove** ModeLabel binding if present (check for `{Binding ModeLabel}`).

### 12. `src/BodyCam/Services/ISettingsService.cs`

**Remove** `Mode` property:
```csharp
// DELETE:
ConversationMode Mode { get; set; }
```

### 13. `src/BodyCam/Services/SettingsService.cs`

**Remove** `Mode` property implementation:
```csharp
// DELETE:
public ConversationMode Mode
{
    get => Enum.TryParse<ConversationMode>(Preferences.Get(nameof(Mode), nameof(ConversationMode.Realtime)), true, out var m)
        ? m : ConversationMode.Realtime;
    set => Preferences.Set(nameof(Mode), value.ToString());
}
```

### 14. `src/BodyCam/MauiProgram.cs`

**Remove** the Mode B comment from IChatClient registration (keep the registration — it's needed for deep_analysis tool in Step 4):
```csharp
// Change comment from "Chat Completions client (Mode B)" to:
// Chat Completions client (used by ConversationAgent for deep_analysis tool)
```

## Files Deleted

### 15. `src/BodyCam/Services/IChatCompletionsClient.cs` — DELETE ENTIRE FILE
### 16. `src/BodyCam/Services/ChatCompletionsClient.cs` — DELETE ENTIRE FILE

The `IChatCompletionsClient` wrapper was only used by Mode B's `ConversationAgent`. In M3, `ConversationAgent` will use `IChatClient` directly (Step 4).

## Verification

```powershell
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-windows10.0.19041.0 --no-restore -v q
```

Build will fail until Step 4 updates `ConversationAgent` to remove `IChatCompletionsClient` dependency. That's expected — Steps 1 and 4 should be applied together or `ConversationAgent` must be temporarily stubbed.

**Alternative (recommended):** Apply Step 1 and Step 4 together as a single atomic change so the build stays green.
