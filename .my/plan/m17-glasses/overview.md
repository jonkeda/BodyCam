# M17 — Glasses Integration

**Status:** Investigation complete — vendor SDK identified  
**Goal:** Connect smart glasses (HeyCyan/TKYUAN/QCSDK, Meta Ray-Ban) as unified
peripheral devices, using the provider abstractions built in M11–M14.

**Depends on:** M11 (camera), M12 (audio input), M13 (audio output), M14 (buttons).

**Vendor SDK integration:** [M33 — HeyCyan Glasses SDK Integration](../m33-heycyan-sdk/overview.md)
delivers the concrete provider implementations and `HeyCyanGlassesDeviceManager`.
The Phase 1 "hardware investigation" portion of M17 is largely satisfied by the
reverse-engineering work already in [`Alternative-HeyCyan-App-and-SDK/`](../../../Alternative-HeyCyan-App-and-SDK/)
— see `WIFI_TRANSFER_ARCHITECTURE.md` and `android/AGENTS.md`. M17 retains the
abstract `GlassesDeviceManager` design and the cross-vendor connection UI; the
HeyCyan-specific BLE/WiFi/transfer plumbing lives entirely in M33.

---

## Context

M11–M14 built the **abstractions** (`ICameraProvider`, `IAudioInputProvider`,
`IAudioOutputProvider`, `IButtonInputProvider`) and the **managers** that switch
between providers. But zero Bluetooth/glasses implementations exist.

M4 (the original glasses plan) predated M11–M14 and designed a monolithic
`IGlassesService`. That design is superseded — we now use the multi-provider
pattern. M17 picks up the **remaining M4 work** that isn't covered elsewhere:

| Original M4 Task | Covered By | M17 Responsibility |
|-------------------|------------|-------------------|
| 4.1 BT audio routing | M12 Phase 2 design | ✅ Implement `BluetoothAudioInputProvider` + `BluetoothAudioOutputProvider` |
| 4.2 Camera investigation | M11 Phase 4 design | ✅ Field research on glasses camera protocol |
| 4.3 Camera bridge | M11 Phase 4 design | ✅ Implement `WifiGlassesCameraProvider` |
| 4.4 IGlassesService | Superseded by M11-M14 | ✅ Replace with `GlassesDeviceManager` (coordinates providers) |
| 4.5 Connection UI | Not covered | ✅ Pair/connect/status UI |
| 4.6 Button mapping | M14 Phase 2 design | ✅ Implement `GattButtonProvider` / `AvrcpButtonProvider` |
| 4.7 Fallback routing | M11-M14 managers | ✅ Wire auto-switch on disconnect |

---

## Architecture

Instead of a monolithic `IGlassesService`, glasses are a **coordinated set of
providers** managed by a `GlassesDeviceManager`:

```
GlassesDeviceManager
  ├── BluetoothAudioInputProvider  → registered with AudioInputManager  (M12)
  ├── BluetoothAudioOutputProvider → registered with AudioOutputManager (M13)
  ├── GattButtonProvider           → registered with ButtonInputManager (M14)
  └── WifiGlassesCameraProvider    → registered with CameraManager     (M11)

When glasses connect → all providers activate → managers auto-switch
When glasses disconnect → providers deactivate → managers fall back to phone
```

### GlassesDeviceManager

```csharp
// Services/Glasses/GlassesDeviceManager.cs
public class GlassesDeviceManager
{
    private readonly AudioInputManager _audioIn;
    private readonly AudioOutputManager _audioOut;
    private readonly ButtonInputManager _buttons;
    private readonly CameraManager _camera;

    public GlassesConnectionState State { get; }
    public GlassesDeviceProfile? ConnectedDevice { get; }
    public GlassesBatteryInfo? Battery { get; }

    public event EventHandler<GlassesConnectionState>? StateChanged;

    /// <summary>Scan for nearby glasses devices.</summary>
    Task<IReadOnlyList<GlassesDeviceInfo>> ScanAsync(CancellationToken ct);

    /// <summary>Connect to a glasses device (activates all applicable providers).</summary>
    Task ConnectAsync(GlassesDeviceInfo device, CancellationToken ct);

    /// <summary>Disconnect (deactivates all providers, managers fall back).</summary>
    Task DisconnectAsync(CancellationToken ct);
}

public enum GlassesConnectionState { Disconnected, Scanning, Connecting, Connected }

public record GlassesDeviceInfo(string Name, string Address, GlassesCapabilities Capabilities);

public record GlassesCapabilities(bool HasCamera, bool HasMic, bool HasSpeaker, bool HasButton);

public record GlassesDeviceProfile(
    string ModelName,        // "TKYUAN-Pro", "RayNeo X2", etc.
    string CameraProtocol,   // "wifi-direct-rtsp", "wifi-direct-mjpeg", "none"
    string AudioProtocol,    // "bt-hfp", "bt-a2dp"
    string ButtonProtocol    // "bt-gatt", "bt-avrcp"
);

public record GlassesBatteryInfo(int Percentage, bool IsCharging);
```

---

## Hardware: HeyCyan / TKYUAN / QCSDK Smart Glasses

**Known specs:**
- Bluetooth 5.3 (BLE control + BT-classic A2DP/HFP audio)
- Camera: 1080p, **photo/video captured to internal storage** (not live-streamed)
- Audio: Open-ear speakers + dual microphone (BT-classic A2DP/HFP)
- Physical button: multi-function (tap, double-tap, long-press) — debounced in firmware
- Storage: internal flash, exposed via WiFi-Direct hotspot in transfer mode
- Vendor SDK: `glasses_sdk_20250723_v01.aar` (Android) + `QCSDK.framework` (iOS)

**Resolved (see [M33](../m33-heycyan-sdk/overview.md) and `Alternative-HeyCyan-App-and-SDK/WIFI_TRANSFER_ARCHITECTURE.md`):**

| Question | Answer |
|----------|--------|
| Camera protocol? | Not a live stream. BLE photo command → glasses opens WiFi-Direct hotspot → HTTP `GET /files/media.config` then `GET /files/<name>.jpg` |
| WiFi-Direct stream? | Hotspot only during transfer mode; reachable IP is from BLE notify frame `0x08` (groupOwnerAddress is the *phone*, not glasses) |
| Button event format? | BLE notify frames (`cmdType=2`) parsed by vendor SDK into tap/double/long-press callbacks |
| Audio codec? | BT-classic A2DP (SBC) + HFP — separate from QCSDK BLE control channel |
| Battery GATT service? | Read via QCSDK `getDeviceBattery` callback (vendor service, not standard 0x180F) |

---

## Phases

### Phase 1: Hardware Investigation & BT Audio
Receive glasses, investigate all protocols, implement BT audio providers.
For HeyCyan/QCSDK hardware the investigation is already complete — see
[`Alternative-HeyCyan-App-and-SDK/`](../../../Alternative-HeyCyan-App-and-SDK/)
and [M33](../m33-heycyan-sdk/overview.md). The remaining work is the generic
`GlassesDeviceManager` skeleton plus the generic `BluetoothAudio*Provider`
implementations from M12/M13 Phase 2 (already done).

**Deliverables:** Investigation report, `BluetoothAudioInputProvider`,
`BluetoothAudioOutputProvider`, basic `GlassesDeviceManager`.

### Phase 2: Buttons & Camera
Implement glasses button provider (GATT or AVRCP) and camera provider
(WiFi-Direct RTSP/MJPEG based on investigation findings).

**Deliverables:** `GattButtonProvider` or `AvrcpButtonProvider`,
`WifiGlassesCameraProvider`, gesture mapping for glasses button.

### Phase 3: Connection UI & Auto-Fallback
Build the connection management UI (scan, pair, connect, status, battery).
Wire auto-fallback when glasses disconnect.

**Deliverables:** GlassesPage XAML, connection flow, status indicators,
auto-fallback wiring, battery monitoring.

### Phase 4: iOS Platform Support
iOS BLE glasses support using `CoreBluetooth` (`CBCentralManager` for scanning,
`CBPeripheral` for GATT services). WiFi glasses camera via
`NEHotspotConfiguration` or direct TCP/RTSP connection. BT audio routing
already handled by M12/M13 iOS phases — this phase wires `GlassesDeviceManager`
to coordinate iOS-specific providers.

**Deliverables:** iOS `CoreBluetooth` BLE scanning + GATT button provider,
iOS WiFi camera stream connection, `GlassesDeviceManager` iOS integration,
`NSBluetoothAlwaysUsageDescription` permission.

---

## Exit Criteria

- [ ] Full conversation + vision loop running through glasses hardware
- [ ] BT audio (mic + speaker) routed through glasses
- [ ] Camera frames received from glasses (protocol determined by investigation)
- [ ] Physical button triggers gesture actions (tap/double-tap/long-press)
- [ ] Graceful fallback when glasses disconnect (auto-switch to phone)
- [ ] Battery level displayed in UI
- [ ] Connection management UI (scan, pair, connect, status)

---

## Documents

| Document | Purpose |
|----------|---------|
| [overview.md](overview.md) | This file — scope, architecture, exit criteria |
| [phase1-investigation-bt-audio.md](phase1-investigation-bt-audio.md) | Phase 1 — Hardware investigation + BT audio providers |
| [phase2-buttons-camera.md](phase2-buttons-camera.md) | Phase 2 — Button + camera providers |
| [phase3-connection-ui.md](phase3-connection-ui.md) | Phase 3 — Connection UI + auto-fallback |
