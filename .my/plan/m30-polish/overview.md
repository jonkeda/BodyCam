# M30 — Polish & Optimization

**Status:** Not started  
**Goal:** Production-ready quality, performance, battery optimization, error
resilience, privacy indicators, and cost tracking.

**Depends on:** Most feature milestones should be complete before polish.

---

## Why M30 (not M6)

Polish is intentionally pushed to the end of the roadmap. It makes no sense to
optimize latency for code that will be rewritten, or add privacy indicators for
features that don't exist yet. All polish items apply across the entire codebase
once the feature set stabilizes.

---

## Phases

### Phase 1: Latency & Performance
Measure and optimize the end-to-end voice round-trip time. Target <500ms from
user-stops-speaking to first TTS audio plays.

**Deliverables:**
- Latency measurement instrumentation (T0-T4 timing points)
- Pre-connect WebSocket on app start
- Stream TTS playback immediately (don't buffer full response)
- Minimize audio chunk sizes (50ms)
- Edge region selection (closest OpenAI endpoint)
- Performance dashboard in debug mode

### Phase 2: Battery & Network Optimization
Minimize power draw for smart glasses use case. Optimize network usage for
metered connections.

**Deliverables:**
- BLE control channel (classic BT only for audio)
- Reduce BT scan frequency when connected
- Keep WebSocket alive with pings (avoid reconnection cost)
- Reduce vision capture frequency based on battery level
- Wake word layer runs at ~10mW (Porcupine)
- Battery profiling on Android

### Phase 3: Error Handling & Resilience
Graceful degradation when things go wrong. Auto-recovery from transient failures.

**Deliverables:**
- WebSocket auto-reconnect with exponential backoff
- API rate limit (429) backoff + lighter model fallback
- API error (500) retry 3x then notify user
- BT disconnect fallback (already in M17, verify here)
- Camera failure → disable vision, notify user
- OOM → trim conversation history, reduce frame resolution
- Offline detection + "I'm offline" voice response
- Command queue for when connection returns

### Phase 4: Settings Page
Centralized settings UI for all user-configurable options.

**Deliverables:**
- Settings page with all options:

| Setting | Type | Default |
|---------|------|---------|
| OpenAI API Key | SecureEntry | (empty) |
| Picovoice AccessKey | SecureEntry | (empty) |
| Chat Model | Picker | gpt-4o-mini |
| Vision Model | Picker | gpt-4o |
| TTS Voice | Picker | alloy |
| Wake Word Enabled | Switch | true |
| Wake Word Sensitivity | Slider | 0.5 |
| Auto Vision | Switch | false |
| Vision Interval (s) | Slider | 10 |
| Dictation Default Mode | Picker | clean |
| Translation Target Language | Picker | (none) |
| Notification Readout | Switch | false |
| Debug Mode | Switch | false |

### Phase 5: Privacy Indicators
Visual and audio cues when camera/mic are active. Critical for trust and
compliance.

**Deliverables:**
- Red dot + "REC" label when mic is active
- Camera icon when vision is capturing
- Short audio tone when recording starts/stops
- Glasses LED control (if supported) during capture
- Privacy policy display in settings

### Phase 6: Cost Tracking
Monitor API usage and estimated cost. Help users avoid surprise bills.

**Deliverables:**
- `UsageTracker` service tracking input/output tokens + vision requests
- Cost estimation based on current model pricing
- Small cost indicator on main page ("$0.12 today")
- Daily/weekly/monthly usage history
- Optional daily budget alert
- Export usage data

### Phase 7: iOS Platform Polish
iOS-specific optimization and App Store compliance.

**Deliverables:**
- iOS battery optimization (Background App Refresh, `BGTaskScheduler`)
- iOS privacy labels for App Store (camera, microphone, Bluetooth, network)
- `NSCameraUsageDescription`, `NSMicrophoneUsageDescription`, `NSBluetoothAlwaysUsageDescription` verified
- iOS performance profiling with Instruments (Time Profiler, Energy Log)
- iOS-specific audio session interruption handling (phone calls, Siri, other apps)
- TestFlight distribution setup

---

## Exit Criteria

- [ ] Voice round-trip < 500ms on good network
- [ ] Battery draw in wake word layer ≤ 15mW
- [ ] Graceful behavior when network drops (auto-reconnect, offline message)
- [ ] Settings page with all user-configurable options
- [ ] Privacy indicators (UI + audio) when recording
- [ ] Cost dashboard showing token usage and estimated cost
- [ ] No crashes on edge cases (OOM, disconnect, rate limit)

---

## Documents

| Document | Purpose |
|----------|---------|
| [overview.md](overview.md) | This file — scope, phases, exit criteria |
