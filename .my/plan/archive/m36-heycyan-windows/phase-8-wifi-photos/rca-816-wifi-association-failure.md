# RCA-816: WiFi Association Failure — Glasses AP Not Visible in Scans

## Summary

After sending `EnterTransferMode` (`[0x02, 0x01, 0x04]`) over BLE, the glasses respond with SSID + password but the AP never becomes visible in Windows WiFi scans. Windows goes `associating → disconnected` on every connect attempt.

## Diagnostic Evidence

From the latest test run:
```
[WIFI] Glasses AP 'M01 Pro_D879B87FE6C9' NOT visible in scan.
Available: SSID 1 :, SSID 2 : jobaboe, SSID 3 : Blacklabel-5G
[WIFI] Interface state @1s: State : associating
[WIFI] Interface state @5s: State : disconnected
```

Key observations:
1. The SSID "M01 Pro_D879B87FE6C9" **never** appears in scan results (waited 40+ seconds)
2. The hidden network "SSID 1 :" is NOT the glasses (persists when glasses are powered off)
3. Windows enters "associating" state then drops to "disconnected" — connecting to nothing
4. The `associating → disconnected` pattern repeats on all 4 attempts
5. User previously saw the SSID in Windows WiFi picker — likely from the HeyCyan phone app putting glasses in transfer mode (different command flow)
6. **The glasses AP never starts at all.** The WiFi radio doesn't turn on.

## Root Cause Analysis

### The iOS SDK Flow (Which Works)

From `GlassesMediaDownloader.m` and `WiFiTransferManager.m`:

```
1. openWifiWithMode:QCOperatorDeviceModeTransfer → get SSID + password
2. waitForHotspotReadiness → poll getDeviceWifiIPSuccess (up to 10 retries with exponential backoff)
3. ONLY when getDeviceWifiIPSuccess returns an IP → proceed to WiFi join
4. NEHotspotConfiguration with SSID + password (WPA2-PSK)
5. Wait 5 seconds for iOS to establish connection
6. Test HTTP connectivity
```

### What We're Missing

**We skip step 2 entirely.** The iOS SDK calls `getDeviceWifiIPSuccess` repeatedly over BLE, which:
- Asks the glasses "is your WiFi hotspot ready?" via BLE
- Only returns an IP when the AP is actually broadcasting
- Uses exponential backoff: 1s, 2s, 4s, 8s... up to 10 retries (~2 min total)

**This is the critical synchronization step.** Without it, we attempt WiFi connection before the AP has finished starting.

### Why "associating → disconnected"

Windows is trying to connect to a network that doesn't exist. The profile is configured correctly but there's no AP broadcasting. The "associating" state is Windows sending directed probes (because nonBroadcast=true) but getting no response, then timing out.

### The Real Problem: AP Never Starts

The BLE command `[0x02, 0x01, 0x04]` causes the glasses to **respond with cached SSID/password credentials** but does NOT actually start the standard WiFi AP. Two theories:

1. **This command only starts WiFi Direct P2P mode** (for Android). The iOS `openWifiWithMode:` may send different BLE bytes that trigger a standard AP.
2. **The AP startup requires follow-up BLE commands** — specifically the `getDeviceWifiIPSuccess` polling. The glasses may wait for this "readiness check" before powering on the WiFi radio (as a power-saving measure).

### Why the User Previously Saw the SSID

Most likely from the **HeyCyan phone app** (iOS/Android) putting the glasses in transfer mode using the proper SDK flow (which includes `getDeviceWifiIPSuccess` or P2P negotiation). The phone app's flow correctly starts the AP.

### Android vs iOS — Platform Difference

- **Android** uses WiFi Direct/P2P (`WifiP2pManagerSingleton`). The `0x08` IP notification is P2P-specific.
- **iOS** uses standard AP mode via `NEHotspotConfiguration`. It polls `getDeviceWifiIPSuccess` to confirm readiness.
- **Both platforms** send the same `[0x02, 0x01, 0x04]` command to start the hotspot.
- **Windows** cannot use WiFi Direct P2P like Android, so we MUST follow the iOS approach.

## What `getDeviceWifiIPSuccess` Does

This is a BLE command inside the compiled QCSDK framework. Based on the pattern:
- It sends a request to the glasses asking for their WiFi IP
- The glasses respond with their assigned IP once the AP is ready
- If the AP isn't ready yet, it fails or returns nil

The Android SDK equivalent is the `0x08` IP notification in `DownloadNotifyListener`. But on Android this comes passively via the P2P flow, while on iOS it's actively polled.

## Plan to Fix

### Phase 1: Reverse-engineer `openWifiWithMode:` from iOS QCSDK binary (REQUIRED)

The iOS SDK's `openWifiWithMode:QCOperatorDeviceModeTransfer` is the command that ACTUALLY starts the standard AP. We need to find what BLE bytes it sends. Options:

**Option A: Sniff BLE traffic from the iOS app**
- Install HeyCyan app on iPhone, connect to glasses
- Use a BLE sniffer (nRF Connect, Wireshark with nRF52840 dongle, or PacketLogger on macOS)
- Trigger transfer mode and capture the exact BLE write sequence
- This gives us both `openWifiWithMode:` and `getDeviceWifiIPSuccess` bytes

**Option B: Static analysis of QCSDK binary**
- The QCSDK framework binary is ARM64 Mach-O
- Search for string references to characteristic UUIDs and trace command construction
- Look for the ObjC method implementation of `+[QCSDKCmdCreator openWifiWithMode:success:fail:]`
- Disassemble with Ghidra/Hopper to find the payload bytes

**Option C: Differential analysis**
- We know `setDeviceMode:` sends `[0x02, 0x01, <mode>]` where mode=0x04 for transfer
- `openWifiWithMode:` is a DIFFERENT method — it likely sends different bytes
- Check if there's a "WiFi open" sub-command vs "mode set" sub-command
- The iOS SDK has BOTH `setDeviceMode:` and `openWifiWithMode:` — they must do different things

### Phase 2: Implement the correct command sequence

Once we have the bytes:
1. Replace `EnterTransferMode` command with the correct `openWifiWithMode` bytes
2. Implement `getDeviceWifiIPSuccess` as a BLE polling command
3. Only attempt WiFi join after BLE confirms the AP is ready
4. Then use the existing profile-based connection approach

### Phase 3: Fallback — longer wait (LOW CONFIDENCE)

Option C from the previous RCA is now deprioritized since the AP clearly never starts. Simply waiting longer won't help if the wrong command is being sent.

## Key Unknowns

1. What are the exact BLE bytes for `openWifiWithMode:` vs `setDeviceMode:`? (Need binary RE or BLE sniff)
2. What are the exact BLE bytes for `getDeviceWifiIPSuccess`? (Need binary RE or BLE sniff)
3. Does `openWifiWithMode:` use the same Serial Port characteristic (0xBC prefix) or a different BLE characteristic?
4. When user saw the SSID before — was the HeyCyan phone app connected at the time?

## Immediate Next Step

**Option B (static analysis)** is fastest since we have the binary. Disassemble the QCSDK Mach-O binary and find the implementation of `+[QCSDKCmdCreator openWifiWithMode:success:fail:]` to extract the BLE payload bytes. Compare with `setDeviceMode:` to identify the difference.
