# iOS SDK WiFi Transfer — Reverse Engineering Notes

**Date:** 2025-05-19  
**Source files:**
- `Alternative-HeyCyan-App-and-SDK/ios/QCSDKDemo/WiFiTransferManager.m`
- `Alternative-HeyCyan-App-and-SDK/ios/QCSDKDemo/GlassesWiFiHandler.m`
- `Alternative-HeyCyan-App-and-SDK/ios/QCSDKDemo/DeviceStatusCheck.m`
- `src/BodyCam.HeyCyan.iOS.Bindings/NativeReferences/QCSDK.framework/Headers/QCSDKCmdCreator.h`
- `src/BodyCam.HeyCyan.iOS.Bindings/NativeReferences/QCSDK.framework/Headers/QCDFU_Utils.h`

---

## Complete iOS BLE → WiFi Transfer Sequence

The iOS SDK (`WiFiTransferManager.m`) documents a **13-step** sequence. Annotated below with BLE opcodes:

### Step 1: Check device is free
```objc
[QCSDKCmdCreator isPeripheralFreeNow]
```
If busy, wait 2s and retry.

### Step 2: Request WiFi Transfer Mode
```objc
[QCSDKCmdCreator openWifiWithMode:QCOperatorDeviceModeTransfer success:^(NSString *ssid, NSString *password) { ... }]
```
- **BLE opcode:** Action `0x41` (GlassesControl), payload `[0x02, 0x01, 0x04]`
  - `0x02` = CmdType prefix
  - `0x01` = Sub-command "set mode"
  - `0x04` = `QCOperatorDeviceModeTransfer` (from enum: Photo=0x01, Video=0x02, VideoStop=0x03, Transfer=0x04)
- **Response:** BLE notification with SSID + password
- **NOTE:** iOS hardcodes password to `"123456789"` regardless of response — comment says "glasses return wrong one"

### Step 3: Poll `getDeviceWifiIPSuccess` (up to 10 retries × 2s)
```objc
[QCSDKCmdCreator getDeviceWifiIPSuccess:^(NSString *ipAddress) { ... } failed:^{ ... }]
```
- **BLE opcode:** Action `0x41` (GlassesControl), payload `[0x02, 0x03]`
  - `0x02` = CmdType prefix
  - `0x03` = CmdTypeIP — queries AP IP
- **Response:** `[0x02, 0x03, IP1, IP2, IP3, IP4, ...]` — extract IPv4 from bytes 2..5
- **Purpose:** Confirms the glasses WiFi AP is initialized. Empty/zero IP = not ready yet.

### Step 4: `getDeviceConfigWithFinished` — CRITICAL VERIFICATION
```objc
[QCSDKCmdCreator getDeviceConfigWithFinished:^(BOOL success, NSError *error, id configData) { ... }]
```
- **BLE opcode:** Action `0x47` (DeviceConfig), **empty payload**
- **Purpose:** According to comments — "CRITICAL VERIFICATION STEP" — verifies device is in WiFi hotspot mode
- **On failure:** wait 2s and RETRY from step 3 (not just step 4)
- **Theory:** This may actually *signal* the glasses to start broadcasting the AP radio. The IP being ready (step 3) doesn't mean the radio is on the air yet.

### Step 5: Wait 5 seconds
```objc
dispatch_after(dispatch_time(DISPATCH_TIME_NOW, (int64_t)(5.0 * NSEC_PER_SEC)), ...)
```
"CRITICAL: Wait for hotspot to actually start broadcasting"

### Step 6: `confirmHotspotIsBroadcastingOverBluetooth` (up to 3 retries × 2s)
```objc
[QCSDKCmdCreator getDeviceWifiIPSuccess:^(NSString *confirmedIP) { ... }]
```
- Same BLE command as step 3 — `GetWifiIP` again
- **Purpose:** Second confirmation that AP is still active after the 5s wait
- If IP is empty: wait 2s, retry (max 3)

### Step 7: Configure WiFi (`NEHotspotConfiguration`)
```objc
NEHotspotConfiguration *configuration = [[NEHotspotConfiguration alloc] initWithSSID:ssid passphrase:password isWEP:NO];
configuration.joinOnce = NO;
configuration.lifeTimeInDays = @1;
```
- First clears any existing config: `removeConfigurationForSSID:`
- Then applies new config
- `isWEP:NO` → this means **WPA/WPA2-Personal** (iOS auto-negotiates WPA vs WPA2)
- `joinOnce = NO` — persist during transfer
- On `NEHotspotConfigurationErrorAlreadyAssociated` → already connected, skip ahead

### Step 8: `getDeviceConfigWithFinished` — Second check (post-WiFi-config)
```objc
[QCSDKCmdCreator getDeviceConfigWithFinished:^(BOOL success, ...) { ... }]
```
- Same `0x47` command again
- Called AFTER iOS WiFi config is applied
- "CRITICAL VERIFICATION STEP — ensures glasses device is still in expected state"
- On success: proceed to 15s wait

### Step 9: Wait 15 seconds (WiFi-dense environment delay)
"Extended delay to handle areas with many competing WiFi networks"

### Step 10: Verify iOS actually joined the network
```objc
[NEHotspotNetwork fetchCurrentWithCompletionHandler:...]
```
- Checks if iOS is on the expected SSID
- Up to 6 retries × 5s
- Re-applies NEHotspotConfiguration on attempts 2 and 4

### Step 11: Test HTTP connection
```objc
NSURLRequest *request = [NSURLRequest requestWithURL:[NSURL URLWithString:testURL] ... timeoutInterval:5.0];
```
- Tests: `/files/media.config`, `/`, `/api/status`, `/config`, `/info`
- Falls back to probing common IPs: `192.168.43.1`, `192.168.4.1`, `192.168.31.1`, etc.

### Step 12: Start media download

---

## Key Insights From iOS Code

1. **Two `getDeviceConfig` (0x47) calls** — one BEFORE WiFi connect, one AFTER. Both are called "CRITICAL". The pre-connect one may actually trigger AP radio activation.

2. **Password is always hardcoded `"123456789"`** — regardless of what the glasses report.

3. **iOS uses `isWEP:NO`** which means WPA/WPA2-Personal with passphrase. iOS auto-negotiates the exact WPA version. This means the glasses could be WPA (TKIP) or WPA2 (AES) — iOS doesn't care.

4. **The 5s wait between `getDeviceConfig` and WiFi connect is critical** — labeled "Wait for hotspot to actually start broadcasting".

5. **iOS re-applies the WiFi config multiple times** — if first join doesn't work, it re-applies on attempts 2 and 4.

6. **`isPeripheralFreeNow` check** — iOS checks this before critical BLE operations and waits if the device is busy.

7. **Total wait time in iOS sequence:** GetWifiIP poll (up to 20s) + getDeviceConfig + 5s + confirm IP + WiFi config + getDeviceConfig + 15s + verify (up to 30s) = potentially **70+ seconds** from EnterTransferMode to actual HTTP request.

---

## BLE Command Reference (WiFi-relevant)

| Command | Action | Payload | Response |
|---------|--------|---------|----------|
| openWifiWithMode:Transfer | 0x41 | `02 01 04` | SSID + password notification |
| getDeviceWifiIPSuccess | 0x41 | `02 03` | `02 03 [IP4]` or empty |
| getDeviceConfigWithFinished | 0x47 | (empty) | success/fail + config data |
| ExitTransferMode | 0x41 | `02 01 09` | ACK |
| isPeripheralFreeNow | N/A | N/A | Local SDK state check |

---

## `GlassesWiFiHandler.m` (Simpler Alternative Path)

A simpler wrapper that does:
1. `openWifiWithMode:Transfer` → get SSID/pwd
2. `NEHotspotConfiguration` with `joinOnce = YES`
3. Wait 5s → test connection by probing IPs

This path does NOT call `getDeviceConfig` or `getDeviceWifiIPSuccess`. It's a simpler but less reliable path.

---

## `DeviceStatusCheck.m`

Just calls `getDeviceConfigWithFinished` and wraps result in a health check object. Used throughout as a "heartbeat" to verify the glasses are responsive.
