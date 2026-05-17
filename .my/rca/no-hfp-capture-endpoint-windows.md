# RCA: No BT capture endpoint for HeyCyan glasses on Windows

**Date:** 2026-05-17  
**Symptom:** After BLE + Classic BT pairing, the HeyCyan glasses speaker (render) endpoint appears but the mic (capture) endpoint does not. Warning logged:  
`Glasses mic unavailable — pair glasses as BT headset in Windows Settings for audio. (No BT capture endpoint matching glasses MAC D8:79:B8:7F:E6:C9.)`

## Root Cause

The M01 Pro glasses are paired with **A2DP only** (Advanced Audio Distribution Profile — render/speaker). Windows has **not** established **HFP** (Hands-Free Profile) which is required for the microphone capture endpoint.

### Evidence from diagnostic test app (`_tmp/stdout.txt`)

**Capture endpoints:** Only built-in devices (Intel mic, Realtek) — **no M01 Pro capture entry**.

**Render endpoints:** `Headphones (M01 Pro_E6C9)` exists with BTHENUM instance path containing A2DP UUID `{0000110B-...}` (AudioSink).

**Paired Classic BT:** M01 Pro_E6C9 at `D8:79:B8:7F:E6:C9`, ClassOfDevice = AudioVideo.

### Why HFP is missing

Windows Bluetooth creates audio endpoints based on the profiles negotiated during pairing:

| Profile | UUID | Creates | Windows endpoint |
|---------|------|---------|-----------------|
| A2DP Sink | `0000110B` | Render | `Headphones (M01 Pro_E6C9)` ✅ |
| HFP HF | `0000111E` | Capture + Render | `Headset (M01 Pro_E6C9)` ❌ missing |

`TryPairClassicAsync` uses `Custom.PairAsync(DevicePairingKinds.ConfirmOnly)` which pairs the device but only connects profiles advertised by the SDP record. If the glasses don't advertise HFP, or if Windows chooses not to connect it, no capture endpoint is created.

Possible reasons:
1. **Glasses firmware does not advertise HFP** — the M01 Pro may only support A2DP for audio output, with mic data flowing over the proprietary BLE/WiFi channel instead.
2. **HFP service is available but Windows didn't auto-connect it** — sometimes requires user to enable "Hands-Free" in Bluetooth device properties.
3. **Pairing was A2DP-only** — `ConfirmOnly` pairing doesn't specify which profiles to connect.

### Code path

```
HeyCyanGlassesDeviceManager.ConnectAsync()
  → _mic.StartAsync(ct)                              // HeyCyanAudioInputProvider
    → _bt.SelectEndpointByMacAsync(mac, ct)           // BluetoothAudioInputProvider
      → scans AudioInputManager.Providers for "bt:{MAC}"
      → WindowsBluetoothEnumerator registered ZERO capture providers for this MAC
      → throws InvalidOperationException("No BT capture endpoint matching glasses MAC …")
  ← caught by DeviceManager, logged as warning
```

The `HeyCyanAudioRouter` also tries for 20 seconds in `ApplyAsync(Connected)`:
```
HeyCyanAudioRouter.ApplyAsync(Connected)
  → RegisterProvider(inputProvider) / RegisterProvider(outputProvider)
  → _inputProvider.IsAvailable == false (no bt:{MAC} in capture providers)
  → waits 10 × 2s polling
  → still false → logs "HeyCyan glasses mic registered but no BT capture endpoint found"
```

## Fix options

### Option A: Accept HFP absence — route mic over BLE/WiFi (preferred if hardware supports it)
If the M01 Pro sends mic audio over its BLE/WiFi data channel (like Android's QCSDK does), the HeyCyan input provider should capture from that channel instead of expecting a Classic BT HFP endpoint.

### Option B: Programmatically connect HFP after pairing
After `Custom.PairAsync`, enumerate the device's SDP records and explicitly connect the HFP profile if advertised. This requires `Windows.Devices.Bluetooth.Rfcomm` APIs.

### Option C: Prompt user to enable Hands-Free in Windows Bluetooth settings
Current behavior — the warning already tells the user to pair as a BT headset. This is a viable UX fallback but poor experience.

### Option D: Skip mic provider registration when HFP is unavailable
Don't call `_mic.StartAsync()` if `HasEndpointWithMac` returns false — avoids the exception and warning. The glasses speaker (A2DP) would still work. Mic would need to fall back to the platform mic.

## Recommendation

Investigate whether the M01 Pro hardware supports HFP at all (check SDP records with the test app). If it does, implement Option B. If not, implement Option A or D depending on whether the glasses have an alternative mic path.
