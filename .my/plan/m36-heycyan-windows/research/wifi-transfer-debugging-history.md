# WiFi Transfer — Debugging History & Observations

**Date:** 2025-05-19  
**Hardware:** HeyCyan M01 Pro, BLE MAC `D8:79:B8:7F:E6:C9`  
**Platform:** Windows 10 (net10.0-windows10.0.19041.0), WiFi interface "Wi-Fi", home SSID "jobaboe"  
**AP details:** SSID `M01 Pro_D879B87FE6C9`, password `123456789`, IP `192.168.31.1`

---

## What Works

- BLE connection to glasses ✅
- `EnterTransferMode` command (`02 01 04`) → glasses respond with SSID + password ✅
- `GetWifiIP` command (`02 03`) → glasses respond with `02-03-C0-A8-1F-01` (192.168.31.1) ✅
- IP parsing logic: extracts IPv4 from `payload[2..5]` ✅
- The glasses DO start their AP (confirmed by user seeing it in Windows WiFi picker ONCE)

## What Fails

- **Windows WiFi association fails every time**
- Pattern A: `associating → disconnected` (2-3s) = WPA handshake rejection
- Pattern B: straight `disconnected` (never even associates) = AP not found/not broadcasting

---

## Experiments Tried

### 1. WPA2PSK + AES profile (initial)
```xml
<authentication>WPA2PSK</authentication>
<encryption>AES</encryption>
<nonBroadcast>false</nonBroadcast>
```
**Result:** `associating → disconnected` in 2-3s. Handshake failure.

### 2. WPAPSK + TKIP profile
```xml
<authentication>WPAPSK</authentication>
<encryption>TKIP</encryption>
<nonBroadcast>false</nonBroadcast>
```
**Result:** `disconnected` immediately. AP not found in scan.

### 3. nonBroadcast=true (hidden network)
```xml
<authentication>WPAPSK</authentication>
<encryption>TKIP</encryption>
<nonBroadcast>true</nonBroadcast>
```
**Result:** Not tested in isolation yet. Currently in codebase.

### 4. Increased delays (15s post-IP wait)
Added 15 second delay after GetWifiIP returns, before attempting WiFi connect.  
**Result:** 8 attempts all showed `disconnected`. AP never appeared in scan.

### 5. Multiple connect attempts (8 attempts)
Each attempt: disconnect → wait 3s → connect → poll state for 10s.  
**Result:** All 8 failed with `disconnected` state.

### 6. Glasses reboot (fresh state)
User rebooted glasses to ensure clean state.  
**Result:** Same failure. BLE sequence works perfectly but WiFi won't connect.

---

## Key Observations

1. **The AP appeared ONCE** in Windows WiFi picker during a test run where a bug in IP parsing caused extended BLE polling (~50 seconds of polling). This suggests the AP needs MUCH longer to start than we're giving it — OR the repeated BLE GetWifiIP polls are what wakes it up.

2. **Scan results never show the glasses SSID** in our automated tests. `netsh wlan show networks` never lists `M01 Pro_D879B87FE6C9`.

3. **All nearby networks show WPA2-Personal** in scan results — but we don't know what the glasses AP actually uses.

4. **iOS `isWEP:NO`** tells us it's WPA/WPA2-Personal with passphrase. iOS auto-negotiates, so the exact WPA version is unknown.

5. **The one time it appeared:** User captured a screenshot showing the AP in the Windows WiFi list. This was during a session where broken IP parsing kept polling BLE for ~50s before giving up. The delay may have been what allowed the AP to fully start.

---

## Gap Analysis: Our Code vs. iOS SDK

| Step | iOS SDK | Our Code | Status |
|------|---------|----------|--------|
| Check device free | `isPeripheralFreeNow` | Not checked | ❌ MISSING |
| EnterTransferMode | `openWifiWithMode:Transfer` | `HeyCyanCommands.EnterTransferMode()` | ✅ |
| Get SSID/password | From BLE response | From BLE notification | ✅ |
| Poll GetWifiIP (10 retries) | `getDeviceWifiIPSuccess` | `PollWifiIpReadyAsync` | ✅ |
| **getDeviceConfig (pre-WiFi)** | `getDeviceConfigWithFinished` (0x47) | **NOT CALLED** | ❌ MISSING |
| **Wait 5s** | Hardcoded delay | **NOT DONE** (we wait 15s later, but at wrong point) | ❌ WRONG TIMING |
| **Confirm IP again** | `getDeviceWifiIPSuccess` again | **NOT DONE** | ❌ MISSING |
| Connect WiFi | NEHotspotConfiguration | netsh WLAN profile | ✅ (mechanism differs) |
| **getDeviceConfig (post-WiFi)** | `getDeviceConfigWithFinished` (0x47) | **NOT CALLED** | ❌ MISSING |
| Wait 15s | Delay for dense environments | We do this BEFORE WiFi connect | ❌ WRONG POSITION |
| Verify connection | Check SSID + HTTP probe | Check interface state + probe IPs | ✅ |

### Critical Missing Steps:
1. **`getDeviceConfig` (0x47) is never called** — iOS calls it TWICE and labels it "CRITICAL"
2. **5s delay is between getDeviceConfig and WiFi connect** — we put our 15s delay before WiFi connect which partially compensates, but the `0x47` command itself may be the trigger
3. **Second IP confirmation** after the 5s wait is missing
4. **Our 15s delay is positioned wrong** — iOS does: GetWifiIP → getDeviceConfig → 5s → confirm IP → WiFi → getDeviceConfig → 15s → verify

---

## Theories For Failure

### Theory 1: `getDeviceConfig` (0x47) is the AP activation signal
The glasses receive `EnterTransferMode` and prepare the AP but don't actually start radio broadcasting until they receive the `0x47` DeviceConfig query. This would explain:
- IP is reported before AP is on air (firmware knows the IP it WILL use)
- AP never appears in scan (radio never activated)
- iOS calling it twice ("critical") suggests it's stateful

### Theory 2: Wrong WPA security algorithm
The glasses might use WPA (not WPA2) or TKIP (not AES). iOS auto-negotiates via `isWEP:NO`. Windows netsh requires exact match in the profile XML.
- Evidence: `associating → disconnected` in 2-3s = handshake failure = wrong algorithm
- Counter: The glasses AP appeared once showing in Windows picker (would show security type), but we didn't capture that info

### Theory 3: AP needs extended startup time
The AP appeared once during a ~50s polling session. Our current sequence gives the AP 15s after IP is reported. Maybe it needs 30-60s.
- Evidence: AP appeared during extended delay
- Counter: iOS only waits 5s after getDeviceConfig (but does many BLE round-trips that add time)

### Theory 4: Repeated BLE polling keeps the AP alive
The iOS SDK makes many BLE round-trips (getDeviceConfig, confirm IP, device checks). Each BLE message may act as a keepalive that prevents the AP from going back to sleep.
- Evidence: AP appeared during heavy BLE polling
- Counter: Speculative

### Theory 5: AP requires ongoing BLE heartbeat to stay broadcasting
The iOS SDK calls `sendVoiceHeartbeatWithFinished` periodically. Without it, the AP might shut down.
- Evidence: None directly
- Counter: The iOS WiFi transfer code doesn't explicitly show heartbeat during transfer
