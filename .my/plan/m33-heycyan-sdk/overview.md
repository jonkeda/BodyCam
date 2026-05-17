# M33 — HeyCyan Glasses SDK Integration

**Status:** IMPLEMENTATION COMPLETE — Hardware Validation Pending
**Goal:** Integrate the vendor **HeyCyan / QCSDK** smart-glasses SDK as the
concrete implementation backing the M11/M12/M13/M14 provider abstractions and
the M17 `GlassesDeviceManager`.

**Depends on:** M11 Phase 1, M12 Phase 1, M13 Phase 1, M14 Phase 1, M17 Phase 1.
**Supersedes:** the speculative protocol assumptions in M11 Phase 4, M17 Phase 1
investigation, and M17 Phase 2 GATT-button design — all replaced with the
actual vendor SDK behavior documented below.

> **Implementation status (2026-04-30):** All seven phases complete.
> Real-hardware acceptance testing pending physical HeyCyan glasses. Manual
> test checklists and integration harness in `TestResults/m33-phase7/2026-04-30/`.

> **Authoritative SDK API names:** see [sdk-api-reference.md](sdk-api-reference.md)
> for the verified Android binding surface (`LargeDataHandler`,
> `BleBaseControl`, `BleOperateManager`, callback interfaces, and command
> byte sequences). All references to vendor symbols in this milestone must
> match that document.

**Sibling reference:** [`Alternative-HeyCyan-App-and-SDK/`](../../../Alternative-HeyCyan-App-and-SDK/)
— vendor SDK binaries, decompiled official app, working CyanBridge sample,
iOS/Android demo apps, and reverse-engineering notes.

---

## Why a Dedicated Milestone

M17 originally treated the Chinese smart glasses as "unknown hardware to be
investigated on arrival." That investigation has effectively already happened
in the `Alternative-HeyCyan-App-and-SDK/` workspace folder, which contains:

- `android/CyanBridge/app/libs/glasses_sdk_20250723_v01.aar` — the official
  Android vendor SDK (the one used by the manufacturer's app).
- `QCSDK.framework/` and `ios/QCSDK.framework/` — the official iOS framework
  (Objective-C, `QCSDKManager`, `QCCentralManager`, `QCSDKCmdCreator`).
- `WIFI_TRANSFER_ARCHITECTURE.md` — confirmed BLE+WiFi-Direct+HTTP protocol.
- `android/AGENTS.md` — confirmed `media.config` HTTP endpoint, OPUS framing,
  `LargeDataHandler.glassesControl` payloads, P2P gotchas.
- `ios/QCSDKDemo/` — Objective-C reference demo (BLE scan, connect, capture,
  WiFi transfer, AI photo).
- `examples/legacy/HeyCyanSwift/` — Swift reference demo.
- `heycyan-core/` — modular Kotlin core libs (`core-ble`, `core-audio`,
  `core-connectivity`, `core-data`, `core-utils`, …).

So M33 is no longer "research" — it is **integration** of a known, working
SDK. This pulls a large body of platform-specific work out of M17 (and the
M11/M12/M13/M14 platform phases) into a single SDK-bridge milestone.

---

## What HeyCyan Glasses Actually Do

This is the architecture the SDK enforces — it is **not** a live RTSP camera
or a generic BLE HID device:

| Capability | Transport | Notes |
|------------|-----------|-------|
| Pairing & control | BLE GATT (QCSDK proprietary service) | `QCCentralManager` (iOS) / `BleBaseControl` + `BleOperateManager` + `LargeDataHandler` (Android) |
| Battery / version / media counts | BLE notify frames | Standard QCSDK callbacks |
| Photo capture | BLE command (`QCOperatorDeviceModePhoto`) | Captured **to internal storage** — not streamed |
| Video capture | BLE command (`Video` / `VideoStop`) | Recorded to internal storage |
| Audio capture | BLE command (`Audio` / `AudioStop`) | Recorded as raw OPUS frames (40-byte fixed packet, often headerless) |
| AI photo | BLE command (`AIPhoto`) | Multi-packet image transfer over BLE notify (`didReceiveAIChatImageData`) |
| Bulk media transfer | BLE → switch to **transfer mode** → glasses open WiFi hotspot → phone joins → HTTP `GET /files/media.config` then `GET /files/<name>` | See `WIFI_TRANSFER_ARCHITECTURE.md`; the phone must `bindProcessToNetwork` to the P2P group |
| Button input | BLE notify frame (`cmdType=2`, parsed payload) | Single physical multi-function button (tap / double / long) |
| Audio I/O during a conversation | BT Classic A2DP (speaker) + HFP/SCO (mic) | Standard BT audio profiles, separate from QCSDK |

### Key Architectural Implications for BodyCam

1. **The glasses are NOT a live camera.** `WifiGlassesCameraProvider` cannot be
   an RTSP/MJPEG client — it must be a *file-based snapshot provider* that
   issues `Photo` over BLE, then retrieves the resulting JPG via the WiFi
   transfer flow. This invalidates the M11 Phase 4 RTSP design.
2. **Two audio paths exist.** Live conversation audio (Realtime API) uses BT
   Classic A2DP+HFP via `BluetoothAudioInputProvider` / `BluetoothAudioOutputProvider`
   (M12/M13 Phase 2 — already implemented). Recorded `.opus` files are a
   *separate* pipeline (post-hoc dictation / voice notes — feeds M16 if
   wanted, not the live mic).
3. **No GATT button characteristic.** The button arrives as a parsed BLE
   notify frame through the SDK callback, not via a generic GATT subscription.
   `GattButtonProvider` (M14) becomes a thin adapter over the QCSDK callback.
4. **Mode switching is exclusive.** Cannot record video and audio
   simultaneously; entering transfer mode interrupts capture mode.
5. **iOS is a binding job.** The framework is Objective-C. We will use a
   .NET binding library (`Microsoft.Maui.Native` / Xamarin-style framework
   bindings) rather than rewriting in Swift.
6. **Android is a binding job.** The AAR is the authoritative implementation;
   we either bind it via `maui-android` AAR binding library or wrap CyanBridge's
   `LargeDataHandler` calls through a thin Kotlin shim.

---

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│  BodyCam shared layer                                       │
│  (M11 ICameraProvider, M12 IAudioInputProvider,             │
│   M13 IAudioOutputProvider, M14 IButtonInputProvider,       │
│   M17 GlassesDeviceManager)                                 │
└────────────────────────┬────────────────────────────────────┘
                         │
┌────────────────────────▼────────────────────────────────────┐
│  Services/Glasses/HeyCyan/                                  │
│  ├── IHeyCyanGlassesSession   (cross-platform interface)    │
│  ├── HeyCyanCameraProvider    (M11 ICameraProvider)         │
│  ├── HeyCyanAudioInputProvider (M12 — BT mic via A2DP/HFP)  │
│  ├── HeyCyanAudioOutputProvider (M13 — BT speaker)          │
│  ├── HeyCyanButtonProvider    (M14 IButtonInputProvider)    │
│  ├── HeyCyanMediaTransfer     (post-hoc OPUS / JPG / MP4)   │
│  └── HeyCyanGlassesDeviceManager : GlassesDeviceManager     │
└────────────────────────┬────────────────────────────────────┘
                         │
        ┌────────────────┴────────────────┐
        │                                 │
┌───────▼──────────────────┐   ┌─────────▼────────────────────┐
│ Platforms/Android/       │   │ Platforms/iOS/               │
│ HeyCyan/                 │   │ HeyCyan/                     │
│ ├── HeyCyanSdkBridge.cs  │   │ ├── HeyCyanSdkBridge.cs      │
│ │   (LargeDataHandler,   │   │ │   (QCCentralManager,       │
│ │    BleBaseControl,     │   │ │    QCSDKCmdCreator,        │
│ │    BleOperateManager,  │   │ │    NEHotspotConfiguration) │
│ │    P2P + HTTP)         │   │ │                             │
│ ├── glasses_sdk.aar      │   │ ├── QCSDK.framework binding  │
│ │   (binding library)    │   │ │                             │
│ └── WiFiP2pHttpClient.cs │   │ └── HotspotHttpClient.cs     │
└──────────────────────────┘   └──────────────────────────────┘
```

### `IHeyCyanGlassesSession`

```csharp
namespace BodyCam.Services.Glasses.HeyCyan;

public interface IHeyCyanGlassesSession : IAsyncDisposable
{
    HeyCyanState State { get; }
    HeyCyanDeviceInfo? Device { get; }

    event EventHandler<HeyCyanState>? StateChanged;
    event EventHandler<HeyCyanBattery>? BatteryUpdated;
    event EventHandler<HeyCyanButtonEvent>? ButtonPressed;
    event EventHandler<HeyCyanMediaCount>? MediaCountUpdated;
    event EventHandler<byte[]>? AiPhotoReceived; // for QCOperatorDeviceModeAIPhoto

    Task<IReadOnlyList<HeyCyanDeviceInfo>> ScanAsync(TimeSpan timeout, CancellationToken ct);
    Task ConnectAsync(HeyCyanDeviceInfo device, CancellationToken ct);
    Task DisconnectAsync(CancellationToken ct);

    Task<HeyCyanVersionInfo> GetVersionAsync(CancellationToken ct);
    Task<HeyCyanBattery> GetBatteryAsync(CancellationToken ct);
    Task SyncTimeAsync(CancellationToken ct);

    Task TakePhotoAsync(CancellationToken ct);
    Task StartVideoAsync(CancellationToken ct);
    Task StopVideoAsync(CancellationToken ct);
    Task StartAudioAsync(CancellationToken ct);
    Task StopAudioAsync(CancellationToken ct);
    Task TakeAiPhotoAsync(CancellationToken ct);

    /// <summary>
    /// Switch to transfer mode and return a working HTTP base URL
    /// (e.g. http://192.168.49.x). Caller is responsible for downloading
    /// /files/media.config and /files/&lt;name&gt; entries.
    /// </summary>
    Task<HeyCyanTransferSession> EnterTransferModeAsync(CancellationToken ct);
}

public enum HeyCyanState { Disconnected, Scanning, Connecting, Connected, TransferMode, Disconnecting }

public sealed record HeyCyanDeviceInfo(string Name, string Address, int Rssi);
public sealed record HeyCyanBattery(int Percentage, bool IsCharging);
public sealed record HeyCyanVersionInfo(string Hardware, string Firmware, string WifiHardware, string WifiFirmware, string MacAddress);
public sealed record HeyCyanMediaCount(int Photos, int Videos, int AudioFiles);
public sealed record HeyCyanButtonEvent(HeyCyanButtonGesture Gesture, DateTimeOffset Timestamp);
public enum HeyCyanButtonGesture { Tap, DoubleTap, LongPress }

public sealed record HeyCyanTransferSession(string BaseUrl, IReadOnlyList<string> FileNames) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => default; // exits transfer mode
}
```

---

## Phases

### Phase 1 — Android SDK Binding & Core Session
Bind `glasses_sdk_20250723_v01.aar` as a .NET for Android binding library.
Implement `HeyCyanSdkBridge` (Android) wrapping `LargeDataHandler` /
`BleBaseControl` / `BleOperateManager` / notify-frame parsing (via
`AddOutDeviceListener`). Implement
`AndroidHeyCyanGlassesSession : IHeyCyanGlassesSession`. Add unit tests with
a fake bridge.

**Deliverables:** `BodyCam.HeyCyan.Android.Bindings` project, `HeyCyanSdkBridge`,
`AndroidHeyCyanGlassesSession`, BLE scan/connect/disconnect, version/battery/
media counts, button event parsing, DI registration on Android.

### Phase 2 — Camera Provider (file-based snapshot)
Implement `HeyCyanCameraProvider : ICameraProvider`. `CaptureFrameAsync`:
issue `TakePhotoAsync` → wait for media-count delta → `EnterTransferModeAsync`
→ download newest JPG → exit transfer mode → return JPG bytes.
Optimization: keep transfer mode warm across consecutive captures with a
short idle timeout to amortize the WiFi-Direct setup cost. Document the
~2-5s end-to-end latency in the M11 docs (vs. <50ms phone camera).

**Deliverables:** `HeyCyanCameraProvider`, `WiFiP2pHttpClient` (Android) and
`HotspotHttpClient` (iOS), `HeyCyanMediaTransfer` helper, latency benchmarks,
integration with `CameraManager` (M11), settings entry.

### Phase 3 — Audio Providers (BT classic, live conversation)
Implement `HeyCyanAudioInputProvider : IAudioInputProvider` and
`HeyCyanAudioOutputProvider : IAudioOutputProvider`. These are *not* QCSDK —
they enumerate the BT Classic A2DP+HFP audio devices that the glasses also
expose (the same way any BT headset does), and select them by MAC-address
match against the QCSDK-paired device. Keep these distinct from the M12/M13
generic `BluetoothAudioInputProvider` only insofar as they auto-pair on
glasses connect and auto-unpair on disconnect.

**Deliverables:** `HeyCyanAudioInputProvider`, `HeyCyanAudioOutputProvider`,
auto-routing on `IHeyCyanGlassesSession.StateChanged`, fallback to phone mic
on disconnect, A2DP codec verification (SBC at minimum).

### Phase 4 — Button Provider
Implement `HeyCyanButtonProvider : IButtonInputProvider` as a thin adapter
that forwards `IHeyCyanGlassesSession.ButtonPressed` events into the M14
`ButtonInputManager`. The SDK already debounces and recognizes
tap/double/long-press, so this provider raises **pre-recognized gestures**
(same path BTHome remotes use — see `m14-buttons/ble-remotes.md`).

**Deliverables:** `HeyCyanButtonProvider`, default gesture-to-action mapping
(tap → start/stop conversation, double-tap → photo, long-press → end
session), settings UI integration, tests.

### Phase 5 — Recorded Media Pipeline (optional, feeds M16)
Wrap `HeyCyanMediaTransfer` for retrieving recorded `.opus` voice notes and
`.mp4` videos as a *post-hoc dictation source*. OPUS framing per
`android/AGENTS.md` (raw 40-byte packets, optionally Ogg-wrap before passing
to the transcription pipeline). This is independent of the live Realtime
conversation flow.

**Deliverables:** OPUS Ogg-wrap helper, MP4 sidecar metadata, MAUI media
gallery page (mirrors `MediaGalleryViewController` in `QCSDKDemo`),
optional hook into M16 dictation.

### Phase 6 — iOS QCSDK.framework Binding
Bind `QCSDK.framework` as a .NET for iOS native framework binding (Objective-C
metadata + ApiDefinition). Implement `IosHeyCyanGlassesSession` wrapping
`QCCentralManager`, `QCSDKManager`, `QCSDKCmdCreator`, and
`NEHotspotConfigurationManager`. Reuse the cross-platform providers from
phases 2-5.

**Deliverables:** `BodyCam.HeyCyan.iOS.Bindings` project, ApiDefinition,
StructsAndEnums, `IosHeyCyanGlassesSession`, `NSBluetoothAlwaysUsageDescription`
+ `NSLocalNetworkUsageDescription` Info.plist entries, parity tests with
Android.

### Phase 7 — `GlassesDeviceManager` Wiring & UI
Wire the HeyCyan providers into the M17 `GlassesDeviceManager`. Build the
connection UI (scan → list → connect → status panel: battery, MAC, firmware,
media counts). Auto-fallback when glasses disconnect (managers already
handle this — just verify end-to-end).

**Deliverables:** `HeyCyanGlassesDeviceManager : GlassesDeviceManager`,
`GlassesPage.xaml` connection flow, status indicators, battery widget on
shell, integration test of full M17 exit criteria using HeyCyan glasses.

### Phase 8 — Devices Page UX Overhaul
Redesign the Devices settings page for a streamlined glasses experience:
auto-select camera/mic/speaker providers on glasses connect (revert on
disconnect), show battery/firmware/MAC inline on Devices page, fix duplicate
labels ("Camera"/"Camera Source", "Audio Input"/"Microphone Source"), move
Test Capture near the Camera Source picker, and add Test Recording / Test
Sound buttons for audio verification.

**Deliverables:** [phase8-devices-page-ux/overview.md](phase8-devices-page-ux/overview.md)

---

## Exit Criteria

> **Status (2026-04-30):** Implementation complete; all criteria require real
> HeyCyan glasses hardware for final sign-off. Manual test checklists prepared
> in `TestResults/m33-phase7/2026-04-30/wave5-checklist-{android,ios}.md`.

- [ ] Android AAR bound; BLE scan/connect works on real glasses *(pending hardware)*
- [ ] iOS framework bound; BLE scan/connect works on real glasses *(pending hardware)*
- [ ] Photo via `HeyCyanCameraProvider` round-trips through `VisionAgent` *(pending hardware)*
- [ ] BT live mic + speaker route through glasses during a conversation *(pending hardware)*
- [ ] Glasses button (tap/double/long) triggers configured actions *(pending hardware)*
- [ ] Auto-fallback to phone camera + mic + speaker on disconnect verified *(pending hardware)*
- [ ] Battery + firmware shown in status panel *(pending hardware)*
- [ ] M17 exit criteria pass end-to-end against HeyCyan hardware *(pending hardware)*
- [ ] (Optional) Recorded `.opus` voice notes import into M16 dictation *(optional, P5)*

**Next steps:** Run `wave5-checklist-{android,ios}.md` on physical HeyCyan
glasses + Android phone + iOS device. When all 11 manual steps pass on both
platforms and integration tests are green with `HEYCYAN_E2E=1`, mark M33 as
✅ COMPLETE.

---

## Non-Goals

- Live RTSP/MJPEG video streaming from the glasses (the hardware does not
  support this).
- Replacing the M14 `BTHome` / `AVRCP` / generic GATT button providers —
  they remain for non-HeyCyan devices.
- Reverse-engineering an alternative to the vendor SDK. We use the
  AAR/framework as shipped.
- Reimplementing P2P/Wi-Fi-Direct logic from scratch on iOS — we reuse
  `NEHotspotConfiguration` exactly as `QCSDKDemo` does.

---

## Documents

| Document | Purpose |
|----------|---------|
| [overview.md](overview.md) | This file |
| [phase1-android-binding.md](phase1-android-binding.md) | AAR binding, `HeyCyanSdkBridge`, Android session |
| [phase2-camera-provider.md](phase2-camera-provider.md) | File-based snapshot camera, P2P+HTTP transfer |
| [phase3-audio-providers.md](phase3-audio-providers.md) | BT A2DP/HFP routing for live conversation |
| [phase4-button-provider.md](phase4-button-provider.md) | Notify-frame button events, pre-recognized gestures |
| [phase5-recorded-media.md](phase5-recorded-media.md) | OPUS / MP4 / JPG retrieval, gallery, M16 hook |
| [phase6-ios-binding.md](phase6-ios-binding.md) | `QCSDK.framework` ApiDefinition, iOS session |
| [phase7-device-manager-ui.md](phase7-device-manager-ui.md) | M17 wiring, connection UI, fallback verification |

## References

- `Alternative-HeyCyan-App-and-SDK/README.md` — SDK overview
- `Alternative-HeyCyan-App-and-SDK/WIFI_TRANSFER_ARCHITECTURE.md` — full transfer protocol
- `Alternative-HeyCyan-App-and-SDK/android/AGENTS.md` — Android-side reverse-engineering notes
- `Alternative-HeyCyan-App-and-SDK/android/CyanBridge/` — working Android sample
- `Alternative-HeyCyan-App-and-SDK/ios/QCSDKDemo/` — working iOS sample (Obj-C)
- `Alternative-HeyCyan-App-and-SDK/examples/legacy/HeyCyanSwift/` — Swift sample
- `Alternative-HeyCyan-App-and-SDK/heycyan-core/` — modular Kotlin core libs
