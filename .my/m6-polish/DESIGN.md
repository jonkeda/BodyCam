# M6 — Polish & Optimization ✦ Quality

**Status:** NOT STARTED
**Goal:** Production-ready quality, performance, and UX.

---

## Scope

| # | Task | Details |
|---|------|---------|
| 6.1 | Latency optimization | Target <500ms voice round-trip |
| 6.2 | Battery optimization | Minimize BT + network drain |
| 6.3 | Offline fallback | Basic commands when no internet |
| 6.4 | Error handling & resilience | Reconnection, graceful degradation |
| 6.5 | Settings page | Model selection, voice settings, privacy controls |
| 6.6 | Privacy indicators | Visual/audio cues when camera/mic active |
| 6.7 | Cost tracking | Token/API usage monitoring |

## Exit Criteria

- [ ] Voice round-trip < 500ms on good network
- [ ] Graceful behavior when network drops
- [ ] Settings page with all user-configurable options
- [ ] Privacy indicators (LED / sound / UI) when recording
- [ ] Cost dashboard showing token usage

---

## Technical Design

### 6.1 — Latency Optimization

**Measurement points:**
```
T0: User stops speaking (VAD detects silence)
T1: Last audio chunk sent to API
T2: First transcript token received
T3: First TTS audio chunk received
T4: First audio sample plays on speaker

Target: T4 - T0 < 500ms
```

**Optimizations:**
- Pre-connect WebSocket on app start (eliminate handshake latency)
- Use server-side VAD (`turn_detection: server_vad`) to reduce round-trips
- Stream TTS playback immediately (don't wait for full response)
- Minimize audio buffer sizes (50ms chunks)
- Consider edge region selection (closest OpenAI endpoint)

### 6.2 — Battery Optimization

**BT:**
- Use BLE for control channel, classic BT only for audio
- Reduce BT scan frequency when connected

**Network:**
- Keep WebSocket alive with pings (avoid reconnection cost)
- Batch small requests where possible
- Reduce vision capture frequency based on battery level

**Audio:**
- Wake word detection uses minimal processing
- Only stream to API when user is speaking

### 6.3 — Offline Fallback

When internet is unavailable:
- Local wake word detection still works
- Queue commands for when connection returns
- Play "I'm offline" message
- Basic local commands: "what time is it?", "battery level?"

**Optional:** Local Whisper model for STT (large download, but fully offline)

### 6.4 — Error Handling & Resilience

| Scenario | Response |
|----------|---------|
| WebSocket disconnect | Auto-reconnect with exponential backoff |
| API rate limit (429) | Back off, switch to lighter model |
| API error (500) | Retry 3x, then notify user |
| BT disconnect | Notify user, fall back to device mic/speaker |
| Camera failure | Disable vision features, notify user |
| OOM | Trim conversation history, reduce frame resolution |

**Reconnection strategy:**
```csharp
async Task ReconnectLoop(CancellationToken ct)
{
    var delay = TimeSpan.FromSeconds(1);
    while (!ct.IsCancellationRequested)
    {
        try
        {
            await _openAi.ConnectAsync(ct);
            return; // success
        }
        catch
        {
            await Task.Delay(delay, ct);
            delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
        }
    }
}
```

### 6.5 — Settings Page

**SettingsPage.xaml with SettingsViewModel:**

| Setting | Type | Default |
|---------|------|---------|
| OpenAI API Key | SecureEntry | (empty) |
| Chat Model | Picker | gpt-5.4-mini |
| Vision Model | Picker | gpt-5.4 |
| Voice | Picker | alloy |
| Wake Word Enabled | Switch | true |
| Auto Vision | Switch | false |
| Vision Interval (s) | Slider | 10 |
| Notification Readout | Switch | false |
| Translation Target Language | Picker | (none) |
| Debug Mode | Switch | false |

### 6.6 — Privacy Indicators

- **UI:** Red dot + "REC" label when mic is active
- **UI:** Camera icon when vision is capturing
- **Audio:** Short tone when recording starts/stops
- **Glasses:** If glasses have LED, illuminate during capture

### 6.7 — Cost Tracking

```csharp
public class UsageTracker
{
    public int TotalInputTokens { get; private set; }
    public int TotalOutputTokens { get; private set; }
    public int VisionRequests { get; private set; }
    public decimal EstimatedCostUsd => CalculateCost();

    public void RecordChatUsage(int inputTokens, int outputTokens) { ... }
    public void RecordVisionUsage() { ... }

    private decimal CalculateCost()
    {
        // gpt-5.4-mini: $0.15 / 1M input, $0.60 / 1M output
        // gpt-5.4: $2.50 / 1M input, $10.00 / 1M output
        // gpt-5.4 vision: ~$0.003 per image (low detail)
        ...
    }
}
```

**UI:** Small cost indicator on main page (e.g., "$0.12 today")

---

## Risks

| Risk | Mitigation |
|------|-----------|
| Can't hit 500ms target | Acceptable at 800ms; optimize later |
| Local Whisper too large for mobile | Make it optional download |
| Users forget about cost | Prominent cost display, daily budget alerts |
