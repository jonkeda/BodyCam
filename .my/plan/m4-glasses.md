# M4 — Bluetooth Glasses Integration ✦ Hardware

**Status:** NOT STARTED
**Goal:** Connect TKYUAN glasses as audio + camera source, replacing laptop peripherals.

---

## Scope

| # | Task | Details |
|---|------|---------|
| 4.1 | BT audio profile connection | Pair glasses, route mic/speaker through BT |
| 4.2 | BT camera investigation | Determine how glasses expose camera |
| 4.3 | Camera bridge service | Adapter to receive camera frames from glasses |
| 4.4 | `IGlassesService` interface | Unified abstraction over glasses capabilities |
| 4.5 | Connection management UI | Pair, connect, status indicator |
| 4.6 | Button mapping | Map glasses physical button to actions |
| 4.7 | Fallback routing | Auto-switch to phone/laptop when glasses disconnected |

## Exit Criteria

- [ ] Full conversation + vision loop running through the glasses hardware
- [ ] BT audio (mic + speaker) routed through glasses
- [ ] Camera frames received from glasses
- [ ] Physical button triggers actions
- [ ] Graceful fallback when glasses disconnect

---

## Technical Design

### Hardware: TKYUAN Smart Glasses

**Known specs (from product class):**
- Bluetooth 5.3
- Camera: 1080p front-facing
- Audio: Open-ear speakers + dual microphone
- Physical button: multi-function (tap, double-tap, long-press)
- Storage: internal or micro-SD

**Unknown (must investigate on arrival):**
- Camera protocol: How is the camera exposed?
  - Option A: Standard BT (unlikely for video, bandwidth too low)
  - Option B: WiFi-Direct stream
  - Option C: Proprietary app / SDK
  - Option D: Camera only records to on-device storage (worst case)
- Button events: How are they exposed over BT?
- Audio codec: SBC, AAC, aptX?

### BT Audio Routing

**Approach:** Glasses appear as a standard BT audio device (A2DP + HFP).

**Windows:**
- Pair glasses via Windows Bluetooth settings
- Audio routing: set glasses as default communication device
- `AudioInputService` automatically uses the default device
- Or: explicitly enumerate and select device by name via NAudio

**Android:**
- Pair via Android BT settings
- Use `AudioManager` to route to BT SCO (Synchronous Connection-Oriented) for mic
- Use `BluetoothHeadset` profile for HFP

### Camera Investigation Strategy

When the glasses arrive, execute this checklist:

1. **Pair and explore BT services:**
   ```
   Use nRF Connect or BT Explorer to list GATT services
   Look for video/image-related service UUIDs
   ```

2. **Check companion app:**
   - Install the manufacturer's app (if any)
   - Capture network traffic (Wireshark) to see protocol
   - Check if app uses WiFi-Direct

3. **WiFi-Direct test:**
   - Check if glasses broadcast WiFi-Direct / hotspot
   - Try connecting and looking for RTSP / HTTP stream

4. **Fallback plans:**
   | Scenario | Solution |
   |----------|---------|
   | BT GATT video service | Read frames via GATT characteristics |
   | WiFi-Direct RTSP | Connect to stream via RTSP client |
   | Proprietary app only | Reverse-engineer protocol or use phone camera |
   | On-device storage only | Mount SD card periodically (not real-time) |

### IGlassesService Interface

```csharp
public interface IGlassesService
{
    // Connection
    Task<bool> ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync();
    bool IsConnected { get; }
    GlassesCapabilities Capabilities { get; }

    // Events
    event EventHandler<GlassesButtonEvent>? ButtonPressed;
    event EventHandler? Disconnected;

    // Camera (if available)
    IAsyncEnumerable<byte[]>? GetCameraFramesAsync(CancellationToken ct);
    Task<byte[]?> CapturePhotoAsync(CancellationToken ct);
}

public record GlassesCapabilities(
    bool HasCamera,
    bool HasMic,
    bool HasSpeaker,
    bool HasButton
);

public enum GlassesButtonEvent
{
    SingleTap,
    DoubleTap,
    LongPress
}
```

### Button Mapping

| Gesture | Action |
|---------|--------|
| Single tap | Push-to-talk (start/stop listening) |
| Double tap | Capture photo + describe |
| Long press | Start/stop session |

### Fallback Routing

```csharp
// In AgentOrchestrator or a new InputRouter service
public IAudioInputService GetAudioInput()
{
    if (_glasses.IsConnected && _glasses.Capabilities.HasMic)
        return _btAudioInput;
    return _defaultAudioInput; // laptop/phone mic
}

public ICameraService GetCamera()
{
    if (_glasses.IsConnected && _glasses.Capabilities.HasCamera)
        return _glassesCameraService;
    return _defaultCameraService; // webcam/phone camera
}
```

---

## UI Updates

Add to MainPage or new GlassesPage:
- BT scan/pair button
- Connection status indicator (icon + text)
- Battery level (if exposed via BT GATT)
- Camera source indicator (glasses / phone / webcam)

---

## Risks

| Risk | Impact | Mitigation |
|------|--------|-----------|
| Camera not accessible | No glasses vision | Use phone camera mounted on glasses frame |
| Poor BT mic quality | Bad transcription | Noise cancellation pre-processing; test codecs |
| Button events not exposed | No hands-free control | Use voice activation only |
| Glasses firmware bugs | Disconnects, crashes | Keep fallback working; order spare unit |
| WiFi-Direct drains battery | Short sessions | Optimize capture frequency |

---

## Pre-Requisites

- [ ] TKYUAN glasses received and charged
- [ ] M1 (audio) and M3 (vision) working with laptop peripherals
- [ ] BT investigation tools ready (nRF Connect, Wireshark)
