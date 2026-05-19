# RCA-815: WiFi Connection Failure — AP Not Ready + Premature Connect

## Symptom

`netsh wlan connect` returns "Connection request was completed successfully" but the WiFi adapter never associates with the glasses AP. After 30 seconds of polling `netsh wlan show interfaces`, the state remains "disconnected" and the glasses SSID never appears.

## Root Cause (Multi-Factor)

### 1. Glasses AP takes 10–20 seconds to start broadcasting

The HeyCyan M01 Pro glasses' WiFi AP does NOT come up instantly after the BLE enter-transfer-mode command. The BLE notification with SSID+password arrives almost immediately (within 100ms), but the **actual WiFi radio requires 10–20 seconds** to start broadcasting.

**Evidence from iOS SDK:**
- The iOS `WiFiTransferManager.m` calls `getDeviceWifiIPSuccess:` — a BLE command that confirms the AP is broadcasting — polling **10 times × 2 seconds = 20 seconds** before even attempting WiFi.
- After confirming AP readiness via BLE, iOS waits **5 more seconds** before applying `NEHotspotConfiguration`.
- Total time from BLE command to WiFi connect attempt: **25+ seconds**.

**Our Windows code:** Attempts `netsh wlan connect` within 1-2 seconds of receiving the SSID notification — the AP isn't up yet.

### 2. `netsh wlan connect` to a non-existent AP fails silently

When `netsh wlan connect` targets an SSID that isn't broadcasting:
- Returns "Connection request was completed successfully" (just means the request was queued)
- The adapter scans for the SSID, can't find it, and goes back to "disconnected"
- No error is surfaced — the adapter simply remains disconnected

### 3. Disconnecting from home WiFi makes things worse

When we disconnect from home WiFi first:
- The WiFi adapter has no active connection
- It enters a low-power scanning state
- Directed probes for hidden SSIDs may be less aggressive
- The adapter can't find the glasses AP (which isn't up yet anyway)
- Result: State remains "disconnected" indefinitely

### 4. Not disconnecting → Windows auto-switches back

When we DON'T disconnect:
- `netsh wlan connect` triggers a brief scan for the glasses SSID
- If the AP isn't ready, the scan fails and Windows stays on home WiFi
- If the AP IS ready but has no internet, Windows may switch back within seconds
- Windows 10/11 aggressively prefers networks with internet connectivity

## Correct Flow (from Android/iOS SDK analysis)

### Android (WiFi Direct P2P)
```
1. Send BLE enter-transfer-mode (0x02, 0x01, 0x04)
2. Get SSID+password from BLE notification
3. Start WiFi P2P peer discovery (timeout: 16s + retry)
4. Connect P2P (WPS Push-Button, timeout: 5s + retry)
5. Bind process to P2P network interface
6. Wait for BLE 0x08 notification with glasses IP
7. Resolve HTTP IP: probe candidates → /24 subnet scan (45s total timeout)
8. Download via network-bound HTTP connection
```

### iOS (Standard WiFi AP)
```
1. Send BLE openWifiWithMode:Transfer
2. Get SSID+password from BLE notification
3. Poll getDeviceWifiIPSuccess via BLE (10 × 2s = 20s) ← CONFIRMS AP IS UP
4. Verify device config via BLE
5. Wait 5 seconds for AP to stabilize
6. Apply NEHotspotConfiguration (joinOnce: YES)
7. Wait 15 seconds for iOS to connect
8. Verify SSID connection (6 × 5s = 30s retry)
9. Reapply configuration if SSID not joined
10. Test HTTP connection to glasses IP
11. If that fails → probe candidate IPs list
```
Total budget: ~70 seconds from BLE command to confirmed HTTP.

### What we did (Windows — BROKEN)
```
1. Send BLE enter-transfer-mode
2. Get SSID+password from BLE notification
3. IMMEDIATELY add profile + connect ← TOO EARLY (AP not up)
4. Check show interfaces (i > 5 → give up) ← TOO SHORT
5. Fail
```

## Missing Pieces

| What | Status | Impact |
|------|--------|--------|
| Wait for AP readiness (10-20s) | ❌ Missing | AP not up when we try to connect |
| BLE `getDeviceWifiIP` command | ❌ Unknown bytes | Can't confirm AP is broadcasting |
| Retry `netsh wlan connect` multiple times | ❌ Only once | Single attempt hits non-existent AP |
| Network interface binding for HTTP | ❌ Missing | HTTP routes through home WiFi, not glasses interface |
| Response validation (reject HTML) | ✅ Fixed | Was hitting home router |
| 0x02 prefix on BLE commands | ✅ Fixed | Commands now correct |

## Fix Plan

### Phase A: Timing fix (immediate)
1. After BLE SSID notification, wait **15 seconds** before first WiFi connect attempt
2. Do NOT disconnect from home WiFi
3. Issue `netsh wlan connect ssid="<SSID>" name="<SSID>" interface="Wi-Fi"` 
4. Check `show interfaces` for SSID — if not connected after 5 seconds, **retry the connect command**
5. Repeat up to 4 times (total ~35 seconds of attempts)
6. Once SSID appears, wait for DHCP IP assignment (additional 5-10 seconds)

### Phase B: Network binding (required for HTTP)
Even when connected to both home WiFi and glasses WiFi, HTTP traffic routes through the "default" interface (usually the one with internet). Must bind HTTP client to the glasses WiFi interface:
- Use `HttpClientHandler.Properties["__SocketLocalEndPoint"]` or a custom `SocketsHttpHandler` with `ConnectCallback` that binds to the Wi-Fi adapter's IP
- OR: temporarily set a lower interface metric on the glasses interface: `netsh interface ip set interface "Wi-Fi" metric=1`

### Phase C: BLE readiness command (ideal but optional)
Figure out what BLE bytes `getDeviceWifiIPSuccess` sends (likely a glasses-control command with sub-action matching 0x08). This would let us confirm the AP is broadcasting before attempting WiFi.

## Glasses HTTP Server Details

| Item | Value |
|------|-------|
| Port | 80 (cleartext HTTP) |
| Media manifest | `GET /files/media.config` |
| File download | `GET /files/{filename}` |
| Manifest format | Plain text, one filename per line |
| Supported types | `.jpg`, `.jpeg`, `.mp4`, `.opus` |
| Authentication | None |
| IP (WiFi Direct) | Dynamic in 192.168.49.x subnet |
| IP (Standard AP) | Likely 192.168.43.1 (Android hotspot default) |
| IP probe order (iOS) | 192.168.43.1, 192.168.4.1, 192.168.31.1, 192.168.1.1, 192.168.0.1, 192.168.100.1, 192.168.123.1, 192.168.137.1, 10.0.0.1, 172.20.10.1 |

## Key Observation from iOS Code

```objc
// GlassesWiFiHandler.m line 75:
self.glassesPassword = @"123456789"; // Force correct password - glasses return wrong one
```

The iOS code **hardcodes** the password to "123456789" and ignores the one the glasses report. This matches our current implementation.
