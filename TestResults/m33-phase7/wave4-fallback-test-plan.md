# M33 Phase 7 Wave 4 — Fallback Verification Test Plan

**Date:** YYYY-MM-DD (fill in when executed)  
**Platform:** [ ] Android [ ] iOS  
**Device:** _____________________  
**Glasses Model:** HeyCyan QCSDK  
**Tester:** _____________________  

## Prerequisites

- [ ] HeyCyan glasses fully charged
- [ ] Glasses paired via Bluetooth settings (not just BLE)
- [ ] BodyCam app installed with M33 Phase 1–7 W1–W3 complete
- [ ] `adb logcat` (Android) or Console.app (iOS) running in a separate terminal
- [ ] Stopwatch or timer app ready

---

## Test Scenario: Unexpected Disconnect During Conversation

### Step 1: Initial Connection

- [ ] Open BodyCam app
- [ ] Navigate to **Glasses** page
- [ ] Tap **Scan** — confirm HeyCyan device appears in list
- [ ] Tap **Connect** — wait for "Connected" status
- [ ] Shell widget (top-right) displays live battery percentage
- [ ] Log entry confirms: `HeyCyan connected (state=Connected)`

**Screenshot:** `step1-connected.png`

---

### Step 2: Start Realtime Conversation

- [ ] Navigate to **Voice** / **Realtime** page
- [ ] Tap **Start Conversation**
- [ ] Verify audio routing:
  - [ ] `AudioInputManager.Active` = `HeyCyanAudioInputProvider` (grep logcat for "Active provider changed")
  - [ ] `AudioOutputManager.Active` = `HeyCyanAudioOutputProvider`
  - [ ] Vision frame source = `HeyCyanCameraProvider`
- [ ] Speak into glasses microphone — agent responds through glasses speaker
- [ ] Log confirms: `Realtime session started with HeyCyan audio paths`

**Screenshot:** `step2-conversation-active.png`

---

### Step 3: Unexpected Disconnect (Physical)

Choose **one** method:
- [ ] **Method A:** Power off glasses (hold button 5 seconds)
- [ ] **Method B:** Walk ≥10 m away through a wall (BLE range exit)

**Start stopwatch NOW** when you trigger the disconnect.

---

### Step 4: Observe Fallback (≤ 2 seconds)

Monitor the app UI and logs:

- [ ] Within **2 seconds**, shell widget disappears or shows "Disconnected"
- [ ] Log entry: `"HeyCyan disconnected — fallback initiated (lastDevice=XX:XX:XX:XX:XX:XX)"`
- [ ] Camera fallback: vision frames now from phone rear camera
- [ ] Mic fallback: agent hears user through phone mic
- [ ] Speaker fallback: agent replies through phone speaker
- [ ] Button fallback: keyboard/volume buttons re-bound
- [ ] **Critical:** Realtime conversation **does NOT drop** — audio simply re-routes

**Measured latencies (stopwatch):**

| Capability       | Target | Measured | Pass/Fail |
|------------------|--------|----------|-----------|
| Camera fallback  | ≤ 2 s  | ______ s | [ ]       |
| Mic fallback     | ≤ 2 s  | ______ s | [ ]       |
| Speaker fallback | ≤ 2 s  | ______ s | [ ]       |
| Button re-bind   | ≤ 1 s  | ______ s | [ ]       |

**Logs (excerpt):**

```
[timestamp] HeyCyan disconnected — fallback initiated (lastDevice=AA:BB:CC:DD:EE:FF)
[timestamp] CameraManager: Active provider changed (heycyan-camera → phone-camera)
[timestamp] AudioInputManager: Active provider changed (heycyan-mic → platform-mic)
[timestamp] AudioOutputManager: Active provider changed (heycyan-speaker → platform-speaker)
```

**Screenshot:** `step4-fallback-complete.png`

---

### Step 5: Verify Toast Notification (Optional)

- [ ] Platform toast/notification appears: _"Glasses disconnected — switched to phone audio"_
- [ ] Notification fires **exactly once** (not per-provider)
- [ ] **Note:** As of implementation, `INotificationService` is not present in the codebase. This step is documented for future implementation.

---

### Step 6: Auto-Reconnect (≤ 30 seconds)

- [ ] Power glasses back on (if you powered them off in Step 3)
- [ ] **Do NOT manually scan/connect** — auto-reconnect should trigger
- [ ] Within **30 seconds**, observe:
  - [ ] Shell widget reappears with live battery %
  - [ ] All four providers switch back to HeyCyan automatically
  - [ ] Log: `"HeyCyan auto-reconnect succeeded (attempt 1/3)"`
- [ ] Realtime conversation still active — audio re-routes back to glasses

**Measured auto-reconnect time:** ______ s (target ≤ 30 s)

**Screenshot:** `step6-auto-reconnect.png`

---

## Failure Modes Observed

Check any that occurred during the test:

- [ ] **None** — all steps passed
- [ ] Realtime audio session broke instead of re-routing → BUG: provider swap ordering issue
- [ ] Provider swap exceeded 2 s → BUG: `StopAsync` blocking on BLE timeout
- [ ] Auto-reconnect never fired → BUG: `_lastDevice` reference lost
- [ ] Exception in logcat/Console → attach full stack trace below
- [ ] Other: _____________________________________________________

---

## Full Log Excerpts

Attach `logcat` (Android) or Console (iOS) output for the entire test run:

```
(paste relevant log lines here)
```

---

## Sign-Off

- [ ] All latency targets met
- [ ] No exceptions in logs
- [ ] Conversation remained audible throughout disconnect/reconnect cycle
- [ ] Test considered **PASS**

**Tester Signature:** _____________________  
**Date Executed:** _____________________  
