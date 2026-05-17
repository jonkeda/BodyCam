# Phase 8 — Devices Page UX Overhaul

**Status:** Proposed  
**Depends on:** Phase 7 (Device Manager UI — **complete**), M36 Phase 4 (Integration)

---

## Problem

The Devices settings page has several UX issues that make the glasses
connection flow confusing:

1. **No auto-selection** — after connecting glasses, the user must manually
   switch Camera, Audio Input, and Audio Output pickers to the glasses
   providers. These should auto-select when glasses connect and revert
   when they disconnect.

2. **Glasses info buried** — battery, firmware, MAC, and media counts are
   only visible on the separate Glasses page. This info should appear
   inline on the Devices page below the Connect button so the user doesn't
   have to navigate away.

3. **Test Capture misplaced** — the "Test Capture" button is at the top
   under "Glasses Camera" but should be near the Camera Source picker since
   it tests whichever camera is active.

4. **Duplicate labels** — "Camera" section has heading "Camera" with
   sub-label "Camera Source" (redundant). "Audio Input" has heading
   "Audio Input" with sub-label "Microphone Source" (also redundant).
   Similarly "Audio Output" → "Speaker".

5. **No audio test buttons** — there's a Test Capture for camera but no
   equivalent for testing audio input (record & playback snippet) or
   audio output (play test tone).

6. **Connect flow disconnected** — "Connect Glasses" navigates away to a
   separate page. Ideally the scan/select/connect flow should be
   streamlined or at least return to Devices automatically.

---

## 8.1 — Auto-select providers on glasses connect

When `HeyCyanGlassesDeviceManager.State` transitions to `Connected`:

- Set `CameraManager.Active` → `HeyCyanCameraProvider` (if available)
- Set `AudioInputManager.Active` → `HeyCyanAudioInputProvider` (if available)
- Set `AudioOutputManager.Active` → `HeyCyanAudioOutputProvider` (if available)

When state transitions to `Disconnected`:

- Revert each manager to the previous/default provider (phone camera,
  platform mic, platform speaker)

**Implementation:** Add `OnGlassesStateChanged` handler in `DeviceViewModel`
or in the managers themselves. The managers already have a priority system —
wire the glasses connect/disconnect to trigger reselection.

```csharp
// In HeyCyanGlassesDeviceManager or DeviceViewModel
private void OnGlassesConnected()
{
    _cameraManager.SetActiveAsync("heycyan-camera");
    _audioInputManager.SetActiveAsync("heycyan-mic");
    _audioOutputManager.SetActiveAsync("heycyan-speaker");
}

private void OnGlassesDisconnected()
{
    _cameraManager.SetActiveAsync("phone");
    _audioInputManager.SetActiveAsync("platform");
    _audioOutputManager.SetActiveAsync("platform");
}
```

---

## 8.2 — Inline glasses info on Devices page

Move the battery/firmware/MAC panel from GlassesPage into a collapsible
section on DeviceSettingsPage, shown when glasses are connected:

```
┌─────────────────────────────────┐
│ [Connect Glasses]               │ ← existing button
│                                 │
│ ┌─ Connected: M01 Pro_E6C9 ──┐ │ ← NEW: inline status
│ │ 🔋 ████████░░  80% ⚡      │ │
│ │ MAC       D8:79:B8:7F:E6:C9│ │
│ │ Firmware  AM01C_2.00.03     │ │
│ │ Hardware  AM01C_V2.0        │ │
│ │ 📷 0  🎬 0  🎙️ 0           │ │
│ │ [Disconnect]                │ │
│ └─────────────────────────────┘ │
│                                 │
│ Camera Source                   │ ← cleaned up label
│ [Picker: HeyCyan Camera    ▾]  │
│ [Test Capture]                  │ ← moved near camera picker
│                                 │
│ Microphone                      │ ← cleaned up label
│ [Picker: HeyCyan Mic       ▾]  │
│ [Test Recording]                │ ← NEW
│                                 │
│ Speaker                         │ ← cleaned up label
│ [Picker: HeyCyan Speaker   ▾]  │
│ [Test Sound]                    │ ← NEW
└─────────────────────────────────┘
```

This requires `DeviceViewModel` to take a dependency on
`HeyCyanGlassesDeviceManager` to expose connection state, battery, etc.

---

## 8.3 — Fix duplicate labels

Current → Proposed:

| Current heading | Current sub-label | Proposed |
|---|---|---|
| "Glasses Camera" | "Status" | Remove section (merged into inline glasses info) |
| "Camera" | "Camera Source" | **"Camera Source"** (single label, no heading) |
| "Audio Input" | "Microphone Source" | **"Microphone"** (single label) |
| "Audio Output" | "Speaker" | **"Speaker"** (single label) |

---

## 8.4 — Move Test Capture near Camera Source

Move the Test Capture button, latency label, and preview image from the
top "Glasses Camera" section to directly below the Camera Source picker.
The test should work for ANY camera source (not just glasses), so its
placement under "Glasses Camera" is misleading.

---

## 8.5 — Add audio test buttons

### Test Recording (Audio Input)

- Record 3 seconds of audio from the selected input provider
- Play it back through the device speaker
- Show a waveform or "Recording… / Playing…" status
- Confirms the mic is working and audible

```csharp
public AsyncRelayCommand TestRecordingCommand { get; }

private async Task TestRecordingAsync()
{
    IsTestingMic = true;
    var buffer = new List<byte[]>();
    // Record 3 seconds
    _audioInputManager.Active.AudioChunkAvailable += (_, chunk) => buffer.Add(chunk);
    await _audioInputManager.Active.StartAsync();
    await Task.Delay(3000);
    await _audioInputManager.Active.StopAsync();
    // Playback through speaker
    foreach (var chunk in buffer)
        _audioOutputManager.Active.PlayChunk(chunk);
    IsTestingMic = false;
}
```

### Test Sound (Audio Output)

- Play a short test tone (440 Hz sine wave, 1 second) through the
  selected output provider
- Confirms the speaker is working

```csharp
public AsyncRelayCommand TestSoundCommand { get; }

private async Task TestSoundAsync()
{
    IsTestingSpeaker = true;
    var tone = GenerateSineWave(440, sampleRate: 16000, durationMs: 1000);
    _audioOutputManager.Active.PlayChunk(tone);
    await Task.Delay(1100);
    IsTestingSpeaker = false;
}
```

---

## 8.6 — Streamline glasses connection flow

Two options (pick one):

**Option A: Inline scan on Devices page** — replace "Connect Glasses"
navigation with an expandable scan section directly on the Devices page.
Scan results appear inline, user taps a device, connection happens in-place.

**Option B: Auto-return** — keep the separate Glasses page but
automatically navigate back to Devices after successful connection.
Add `Shell.Current.GoToAsync("..")` after `ConnectAsync` completes.

**Recommendation:** Option B is simpler and lower-risk. Option A is better
UX but requires more refactoring.

---

## Acceptance

- [ ] Camera/Audio Input/Audio Output auto-select to glasses providers on connect
- [ ] Providers revert to defaults on glasses disconnect
- [ ] Battery, MAC, firmware shown inline on Devices page when connected
- [ ] Disconnect button on Devices page (no need to navigate to Glasses page)
- [ ] "Camera Source" / "Microphone" / "Speaker" — no duplicate headings
- [ ] Test Capture moved below Camera Source picker
- [ ] Test Recording button for audio input (record 3s → playback)
- [ ] Test Sound button for audio output (play 440 Hz tone)
- [ ] Glasses page still accessible for advanced info / scan
- [ ] No regression on existing functionality
