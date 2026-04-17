# M1 Implementation — Step 9: End-to-End Voice Loop + Manual Testing

**Depends on:** Steps 1-7 complete (all components wired)
**Produces:** Working end-to-end voice conversation on Windows

---

## Why This Step?
This is the integration milestone. All components exist — now verify they work together as a complete voice loop: speak → hear AI response → see transcripts.

---

## Tasks

### 9.1 — Temporary API key entry (until M6 settings page)

Since there's no settings page yet, add a simple way to enter the API key on first launch. Options:

**Option A: Environment variable (dev-only)**
```csharp
// In MauiProgram.cs or App.xaml.cs startup
var apiKeyService = app.Services.GetRequiredService<IApiKeyService>();
if (!apiKeyService.HasKey)
{
    var envKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
    if (!string.IsNullOrEmpty(envKey))
        await apiKeyService.SetApiKeyAsync(envKey);
}
```

**Option B: Simple prompt dialog on MainPage**
```csharp
// In MainPage.OnAppearing or MainViewModel
if (!_apiKeyService.HasKey)
{
    var key = await Application.Current!.Windows[0].Page!
        .DisplayPromptAsync("API Key", "Enter your OpenAI API key:", placeholder: "sk-proj-...");
    if (!string.IsNullOrWhiteSpace(key))
        await _apiKeyService.SetApiKeyAsync(key);
}
```

**Decision: Use both.** Environment variable for dev/CI, prompt dialog as fallback.

### 9.2 — Update MainPage UI for richer transcript display

The current UI appends to a single string. Enhance slightly:
- Show "Connecting..." status after pressing Start
- Show "Listening..." when connected and mic is active
- Show "AI speaking..." indicator when audio delta events arrive
- Clear transcript button

### 9.3 — Manual test script

Run through this sequence on Windows:

1. **Launch app** → should show Start button
2. **Press Start** → should show "Connecting..." then "Listening..."
3. **Say "Hello, how are you?"** → should see:
   - "You: Hello, how are you?" in transcript
   - AI response text appearing in transcript
   - Hear AI voice through speakers/headphones
4. **While AI is speaking, say something** → should:
   - Stop AI audio immediately (interruption)
   - Start new response
5. **Press Stop** → should:
   - Stop mic capture
   - Stop audio playback
   - Disconnect WebSocket
   - Show "Start" button again
6. **Press Start again** → should reconnect and work

### 9.4 — Debug logging verification

Check the debug console shows:
- `[HH:MM:SS] Realtime connected.`
- `[HH:MM:SS] Audio pipeline started.`
- `[HH:MM:SS] User said: {transcript}`
- `[HH:MM:SS] AI said: {transcript}`
- `[HH:MM:SS] Interrupted at {N}ms.` (when interrupting)
- `[HH:MM:SS] Response complete: {id}`
- `[HH:MM:SS] Orchestrator stopped.`

### 9.5 — Error handling verification

Test these error scenarios:
1. **No API key** → should show error, not crash
2. **Invalid API key** → should show `ErrorOccurred` with 401 message
3. **Network disconnect** → should surface error, allow restart
4. **Stop while speaking** → should stop cleanly without exceptions

### 9.6 — Latency measurement

Use the debug log timestamps to measure:
- Time from "Audio pipeline started" to first "User said" (mic→OpenAI→transcript)
- Time from "User said" to first audio output (AI thinking + TTS start)
- Target: < 500ms voice round-trip for gpt-5.4-realtime-mini

---

## Verification (Exit Criteria for M1)

- [ ] **Speak into laptop mic → see transcript on screen** ✦
- [ ] **Hear AI response through speakers** ✦
- [ ] Interruption handling works (speak while AI is talking)
- [ ] Session survives multiple turns (5+ back-and-forth exchanges)
- [ ] Clean start/stop/restart cycle
- [ ] Error scenarios handled gracefully
- [ ] Debug log shows full event flow
- [ ] No memory leaks (check after 10+ minutes of conversation)
- [ ] API key never appears in debug log or transcript

---

## Files Changed

| File | Action |
|------|--------|
| `MauiProgram.cs` or `App.xaml.cs` | MODIFY — add env var key loading |
| `ViewModels/MainViewModel.cs` | MODIFY — add key prompt, status indicators |
| `MainPage.xaml` | MODIFY — add status label, clear button |

---

## What's NOT in M1

These are explicitly out of scope for M1:
- Settings page (M6)
- Key validation via GET /v1/models (M7.4, ships with M6)
- Azure OpenAI backend (M7.6, future)
- Push-to-talk mode (M5)
- Wake word detection (M5)
- Camera/vision integration (M3)
- Android testing (Step 8 provides the code, but full Android testing is separate)
