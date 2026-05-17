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

### Phase 8: App Icon & Branding
Custom app icon that communicates the product identity at a glance. Replaces the
default MAUI template icon on all platforms.

**Concept direction:**

The icon should convey *wearable AI assistant* — smart glasses + voice. A few
candidate directions:

| # | Concept | Description |
|---|---------|-------------|
| 1 | **Glasses silhouette + waveform** | Minimal side-profile of smart glasses with a small audio waveform underneath or inside one lens. Clean, recognizable at small sizes. |
| 2 | **Lens with AI spark** | Single circular lens (front-on) with a subtle sparkle/star inside, suggesting intelligence. Bold, app-store friendly. |
| 3 | **Eye + signal** | Stylized eye shape combined with broadcast/signal arcs, implying vision + connectivity. |
| 4 | **Monogram "BC"** | Lettermark in a rounded-square with a gradient that evokes glass/light refraction. Simple, professional. |

**Recommended:** Option 1 — it directly communicates both *glasses* and *voice*,
the two core concepts of the app.

**Color palette:**
- Primary: deep navy (#1A2744) or charcoal — professional, pairs well with light accents
- Accent: cyan/teal (#00BCD4) — ties to the HeyCyan brand and feels tech-forward
- Background: solid color or subtle gradient (no transparent backgrounds for store icons)

**Deliverables:**
- SVG source file for the icon (vector, scalable)
- Platform-specific exports:
  - **Android:** adaptive icon (foreground + background layers), 108×108dp foreground, mipmap set
  - **iOS:** 1024×1024 App Store icon + required sizes (no alpha channel)
  - **Windows:** 44×44, 150×150, store logo 50×50 (`.ico` or `.png`)
  - **MAUI:** replace `appicon.svg` + `appicon_fg.svg` in `Resources/AppIcon/`
- Splash screen updated to match icon branding
- Verify icon renders correctly at 16×16, 32×32, 64×64, 128×128, 512×512

**Implementation steps:**
1. Generate icon candidates using an image generation tool or designer
2. Review at multiple sizes (notification badge → app store listing)
3. Export platform assets
4. Replace default MAUI icon files in `src/BodyCam/Resources/AppIcon/`
5. Update `Info.plist` / `AndroidManifest.xml` if needed
6. Test on physical devices (Android, iOS, Windows)

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
