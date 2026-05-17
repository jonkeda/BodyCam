# Phase 6 Research: HeyCyan M01 Audio on Windows

**Date**: 2026-05-16

## Key Finding

**Audio does NOT stream over BLE GATT characteristics.** The BLE connection is
only for commands/notifications. Audio uses entirely separate transports.

---

## Two Audio Pathways

### Pathway A: Live Microphone (BT Classic HFP/SCO)

Real-time microphone audio flows over **Classic Bluetooth HFP/SCO**, not BLE.

| Detail | Value |
|---|---|
| Transport | BT Classic — HFP (Hands-Free Profile) / SCO link |
| Codec | SBC / mSBC (HFP 1.6+) / CVSD fallback |
| Format | PCM `byte[]` chunks via OS audio APIs |
| Android | `AudioRecord(VOICE_COMMUNICATION)` + `startBluetoothSco()` or `setCommunicationDevice()` |
| iOS | `AVAudioSession(.record, .allowBluetooth)` + `SFSpeechRecognizer` |
| Windows | `IBluetoothAudioInputProvider` → `SelectEndpointByMacAsync(mac)` |

**How Android connects audio:**
```kotlin
// CyanBridge: MeetingCaptureService.kt
val audioSource = when (source) {
    CaptureSource.BLUETOOTH_MIC -> MediaRecorder.AudioSource.VOICE_COMMUNICATION
    CaptureSource.PHONE_MIC -> MediaRecorder.AudioSource.MIC
}
// Looks for TYPE_BLUETOOTH_SCO or TYPE_BLE_HEADSET (Android 12+ LE Audio)
audioManager.startBluetoothSco()  // or setCommunicationDevice() on API 31+
```

**How iOS connects audio:**
```swift
// SpeechRecognitionManager.swift
let audioSession = AVAudioSession.sharedInstance()
try audioSession.setCategory(.record, mode: .measurement, options: [.allowBluetooth])
```

Both platforms:
1. Connect to glasses via BLE (using QCSDK)
2. Route audio via OS Bluetooth audio APIs (SCO/HFP or LE Audio)
3. The BLE and audio connections are **independent** — BLE for commands, BT Classic for audio

### Pathway B: On-Glasses Recording (Opus, WiFi Transfer)

The glasses record audio internally, then files are downloaded over WiFi.

| Detail | Value |
|---|---|
| Trigger | BLE command: `0x02, 0x01, 0x08` (start) / `0x02, 0x01, 0x0c` (stop) |
| Storage | On-glasses flash storage |
| Format | Raw Opus packets (40-byte fixed blocks, NO Ogg container) |
| Transfer | WiFi Direct HTTP: `http://<glasses-ip>/files/<name>.opus` |
| Status in BodyCam | Not implemented (M33 Phase 5 placeholder) |

```csharp
// HeyCyanCommands.cs
public static byte[] StartAudioRecording() => new byte[] { 0x02, 0x01, 0x08 };
```

---

## Why Windows Can't Find the Glasses for BT Classic Pairing

The M01 glasses advertise only via **BLE** by default (found by our BLE scanner
as `M01 Pro_E6C9`). They do NOT appear in Windows Settings → "Add Bluetooth
device" because that UI scans for **Classic BT** discoverable devices.
The glasses must be manually put into Classic BT pairing mode (hold power 5s)
to become discoverable for Classic BT pairing. See [Phase 7](../phase-7-classic-bt-pairing/overview.md).

On Android/iOS, this works because:
- The QCSDK AAR/framework connects via BLE first
- Android then uses `TYPE_BLE_HEADSET` (LE Audio) or the SDK internally
  triggers Classic BT pairing
- iOS routes audio with `.allowBluetooth` which handles both profiles

On Windows:
- We connect via BLE GATT (working)
- But there's no Classic BT pairing → no audio endpoint
- `HasEndpointWithMac(mac)` returns `false` → `InvalidOperationException`

---

## GATT Services (Confirmed via Enumeration)

None of these carry audio data:

| Service UUID | Purpose | Characteristics |
|---|---|---|
| `6e40fff0-b5a3-f393-e0a9-e50e24dcca9e` | Nordic UART (commands) | `6e400002` (Write), `6e400003` (Notify) |
| `de5bf728-d711-4e47-af26-65e3012a5dc7` | Serial Port (commands) | `de5bf72a` (Write), `de5bf729` (Notify) |
| `0000ae30-...` | Vendor control | `ae01-ae05`, `ae10` |
| `0000ae3a-...` | Vendor secondary | `ae3b` (Write), `ae3c` (Notify) |
| `00003802-...` | Vendor | `4a02` (Read/Write/Notify) |
| `0000fee1-...` | Vendor | `fee3` (Read/Write/Notify) |
| `00001800-...` | GAP (standard) | `2a00` (Device Name) |
| `00001801-...` | GATT (standard) | `2a05` (Service Changed) |
| `0000180a-...` | Device Info (standard) | `2a25-2a27`, `2a23` |

The `core-audio` module in `heycyan-core` is an **empty stub** (marker class only).

---

## Options for Windows Audio

### Option 1: Programmatic BLE Pairing → Classic BT Audio ⭐ Recommended

Use `DeviceInformation.Pairing.PairAsync()` after GATT connection to
programmatically pair the glasses. If successful, Windows may recognize
them as a Bluetooth audio device and create an audio endpoint.

```csharp
var bleDevice = await BluetoothLEDevice.FromBluetoothAddressAsync(address);
if (!bleDevice.DeviceInformation.Pairing.IsPaired)
{
    var result = await bleDevice.DeviceInformation.Pairing.PairAsync();
    // PairingResultStatus.Paired → audio endpoint should appear
}
```

**Pros:** Reuses existing `HeyCyanAudioInputProvider` and `IBluetoothAudioInputProvider`
**Cons:** May not work if glasses don't expose HFP/SCO after BLE pairing
**Risk:** Medium — depends on M01 hardware capabilities

### Option 2: On-Glasses Recording (Opus → WiFi Transfer)

Use BLE commands to start/stop recording on the glasses, then download
the `.opus` files via WiFi Direct HTTP transfer.

**Pros:** Known working (Android CyanBridge implements this)
**Cons:** Not real-time — latency of record + transfer; requires WiFi Direct (Phase 5)
**Risk:** Low — well-documented protocol

### Option 3: LE Audio (BLE Audio Profile)

Windows 11 22H2+ has limited LE Audio support. The CyanBridge references
`TYPE_BLE_HEADSET` which is Android's LE Audio type. If the M01 supports
LE Audio with LC3 codec, Windows might handle it natively.

**Pros:** Native OS support, no custom codec work
**Cons:** Requires Windows 11 22H2+, LE Audio support is immature on Windows
**Risk:** High — may not be supported by M01 hardware or Windows BT stack

---

## Implementation Plan

1. **Try Option 1 first** — programmatic pairing after GATT connect
2. **Probe for audio endpoint** — check if pairing creates an audio capture device
3. **If no audio endpoint** — fall back gracefully with warning (current behavior)
4. **Document results** for future Option 2/3 implementation
