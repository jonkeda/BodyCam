# SDP Investigation Results — M01 Pro HFP

**Date:** 2025-05-17  
**Status:** Confirmed — HFP advertised, not connected

---

## Summary

The diagnostic test app (`_tmp/Program.cs`) was extended with SDP/RFCOMM
service enumeration to determine whether the M01 Pro glasses advertise HFP.

**Result: The M01 Pro _does_ advertise HFP Hands-Free.** The hardware supports
mic audio over Classic BT. The problem is that Windows paired the device and
connected A2DP (render) but did **not** establish the HFP connection (capture).

---

## SDP Records — M01 Pro_E6C9 (D8:79:B8:7F:E6:C9)

```
RFCOMM services found: 2

1. HFP Hands-Free (0000111e-0000-1000-8000-00805f9b34fb)  ← MIC
   Host: (D8:79:B8:7F:E6:C9)
   Service: Bluetooth#Bluetooth5c:b4:7e:35:4f:07-d8:79:b8:7f:e6:c9
            #RFCOMM:00000000:{0000111e-...}

2. Serial Port / SPP (00001101-0000-1000-8000-00805f9b34fb)
   Host: (D8:79:B8:7F:E6:C9)
   Service: Bluetooth#Bluetooth5c:b4:7e:35:4f:07-d8:79:b8:7f:e6:c9
            #RFCOMM:00000000:{00001101-...}
```

### Notable absences

- **No A2DP AudioSink** (`0000110b`) — The render endpoint exists via
  MMDevice, but the SDP query for A2DP didn't return a hit. A2DP uses L2CAP
  (not RFCOMM), so `GetRfcommServicesAsync` wouldn't find it. The render
  endpoint is confirmed working in MMDevice enumeration.
- **No AVRCP** — Not unexpected for smart glasses.

---

## Comparison with known-good HFP devices

| Device | HFP (111e) | SPP (1101) | Other |
|---|---|---|---|
| **M01 Pro_E6C9** | ✓ | ✓ | — |
| 1MORE HQ51 | ✓ | ✓ | — |
| 1MORE SonoFlow | ✓ | ✓ | Custom vendor UUID `66666666-...` |
| Tesla Model Y | — | — | 0 services (not audio) |

The M01 Pro has the same profile set as standard BT headsets that work with
Windows audio capture.

---

## Root Cause (refined)

The original Phase 7 overview hypothesized that Classic BT pairing was missing.
The SDP investigation clarifies the issue:

1. **Classic BT _is_ paired** — the device appears in `BluetoothDevice`
   enumeration as paired, and we can query its RFCOMM services.
2. **A2DP _is_ connected** — a render endpoint exists under
   `BTHENUM\Dev_D879B87FE6C9`.
3. **HFP is _not_ connected** — no capture endpoint exists despite the device
   advertising HFP Hands-Free (`0000111e`).

This means the pairing step from Phase 7.2 is already done (either manually or
via `TryPairClassicAsync`). The gap is that Windows chose to connect only A2DP
and not HFP. This can happen when:

- The OS auto-connects only the "media" profile (A2DP) and skips
  "telephony" (HFP) unless explicitly requested.
- The device was paired while not in a state to accept HFP connections.
- Windows Bluetooth stack decided HFP negotiation wasn't needed.

---

## Fix Approach

Phase 7 needs an additional step **after** pairing: explicitly connect the
HFP profile. Two options:

### Option A — Programmatic HFP RFCOMM connect (preferred)

Open the HFP RFCOMM service to force Windows to establish the SCO audio
channel:

```csharp
private async Task<bool> TryConnectHfpAsync(BluetoothDevice device, CancellationToken ct)
{
    var hfpId = RfcommServiceId.FromUuid(
        Guid.Parse("0000111e-0000-1000-8000-00805f9b34fb"));
    var services = await device.GetRfcommServicesForIdAsync(hfpId)
        .AsTask(ct);

    if (services.Services.Count == 0)
        return false;

    var service = services.Services[0];

    // Opening a StreamSocket to the HFP RFCOMM channel triggers Windows
    // to set up the SCO audio link and create the capture endpoint.
    using var socket = new StreamSocket();
    await socket.ConnectAsync(
        service.ConnectionHostName,
        service.ConnectionServiceName,
        SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication)
        .AsTask(ct);

    // Keep socket alive briefly to let Windows complete audio setup
    await Task.Delay(2000, ct);

    return true;
}
```

**Risk:** Opening a raw RFCOMM socket to HFP may not be enough — Windows
may need the AT command handshake (HFP init sequence: `AT+BRSF`, `AT+CIND`,
etc.) before it creates the audio endpoint. Needs testing.

### Option B — Windows Audio Policy trigger

Use `Windows.Media.Devices.MediaDevice` or the audio policy APIs to request
that Windows connect the "communications" device for the BT address. Less
documented but may work via:

```csharp
var defaultCommId = Windows.Media.Devices.MediaDevice
    .GetDefaultAudioCaptureId(AudioDeviceRole.Communications);
```

### Option C — Audio Switching API

Use `AudioPlaybackConnection` (Windows 10 2004+) to explicitly request audio
routing from a specific BT device. This API was designed for exactly this
scenario.

---

## Next Steps

1. ~~**Test Option A**~~ — RFCOMM socket to HFP blocked by Windows with
   `WSAEACCES` (`0x8007277C`). HFP is a system-managed profile; the Windows
   BT audio driver owns the RFCOMM channel and blocks direct app access.

2. ~~**Test Option C**~~ — `AudioPlaybackConnection` uses the A2DP
   **AudioSource** UUID (`0000110A`) — it's for receiving audio from a remote
   source device, not for connecting to a headset/glasses. M01 Pro doesn't
   appear as an APC-compatible device. `TryCreateFromId` with the raw BT
   device ID throws `InvalidCastException` (`0x80004002`).

3. **Option B — Windows audio policy/default device**: Investigate whether
   setting the BT device as the default communications device forces Windows
   to connect HFP.

4. **Manual test**: Unpair M01 Pro from Windows Settings, re-pair manually.
   If Windows creates both A2DP render AND HFP capture endpoints on manual
   pair, the issue is that programmatic pairing (`ConfirmOnly`) doesn't
   trigger full profile negotiation.

5. **Registry/driver approach**: The Windows BT audio driver
   (`BthA2dp.sys` / `BthHfpAudio.sys`) controls profile connections. Explore
   whether triggering a device disable/enable cycle or writing to the BT
   profile registry keys forces HFP activation.

6. **Alternative**: Route mic audio over BLE GATT or WiFi instead of
   relying on HFP. The HeyCyan SDK may support audio streaming over the
   data channel.
