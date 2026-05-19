# WiFi Transfer — Step-by-Step Fix Plan

**Goal:** Get Windows to successfully connect to the HeyCyan M01 Pro WiFi AP and download photos.

---

## Plan Overview

The iOS SDK shows a precise multi-step BLE choreography that we're largely skipping. The most likely root cause is that we never send `getDeviceConfig` (action `0x47`) which appears to be the actual AP radio activation signal. Secondary issues are wrong WPA profile settings and timing.

---

## Step 1: Add `getDeviceConfig` (0x47) call after GetWifiIP

**Rationale:** iOS calls this immediately after getting the IP and labels it "CRITICAL VERIFICATION STEP". If the glasses firmware uses this as the signal to turn on the WiFi radio, not sending it means the AP never broadcasts.

**Implementation:**
- We already have `HeyCyanCommands.GetDeviceConfig()` defined (action `0x47`, empty payload)
- After `PollWifiIpReadyAsync` returns successfully, send `GetDeviceConfig` via BLE
- Wait for notification response on action `0x47`
- On success: proceed to wait
- On failure: retry from GetWifiIP

**Expected response from glasses:** Unknown payload format — just check for any response on action `0x47`.

---

## Step 2: Wait 5 seconds after getDeviceConfig

**Rationale:** iOS code says "CRITICAL: Wait for hotspot to actually start broadcasting after config check". This is the time for the radio to spin up.

**Implementation:**
- After successful `getDeviceConfig` response, wait exactly 5 seconds
- Then proceed to step 3

---

## Step 3: Confirm IP again via second GetWifiIP call

**Rationale:** iOS calls `confirmHotspotIsBroadcastingOverBluetooth` which is just another `getDeviceWifiIPSuccess` call. If the AP crashed during startup, this catches it.

**Implementation:**
- Send `GetWifiIP` again after the 5s wait
- If IP is still valid → AP confirmed broadcasting
- If empty/zero → AP failed, retry from step 1 (max 3 retries)

---

## Step 4: Try both WPA and WPA2 profiles

**Rationale:** iOS uses `isWEP:NO` which auto-negotiates WPA vs WPA2. Windows requires exact match. We've tried WPA2+AES (got `associating → disconnected` = handshake failure) and WPAPSK+TKIP (got `disconnected` = AP not found). The `associating` with WPA2 suggests the AP IS WPA2 but something else is wrong with the handshake.

**Implementation — try BOTH profiles in sequence:**

Profile A (most likely — WPA2 Personal):
```xml
<authentication>WPA2PSK</authentication>
<encryption>AES</encryption>
<nonBroadcast>true</nonBroadcast>
```

Profile B (fallback — WPA Personal):
```xml
<authentication>WPAPSK</authentication>
<encryption>TKIP</encryption>
<nonBroadcast>true</nonBroadcast>
```

Profile C (nuclear option — open network):
```xml
<authentication>open</authentication>
<encryption>none</encryption>
<nonBroadcast>true</nonBroadcast>
```

Try A first. If `associating → disconnected` persists, switch to B. If both fail, try C (unlikely but cheap to test).

**Key change:** Use `nonBroadcast=true` for ALL profiles since the AP may be hidden (iOS `NEHotspotConfiguration` can join by SSID without broadcast).

---

## Step 5: Send second `getDeviceConfig` AFTER WiFi connect attempt

**Rationale:** iOS calls this a second time after `applyConfiguration` completes, before the final 15s wait. It may keep the glasses in transfer state (prevent timeout/sleep).

**Implementation:**
- After `netsh wlan connect` is issued (regardless of immediate result), send `getDeviceConfig` over BLE
- This keeps the BLE channel active and confirms glasses are still in transfer mode

---

## Step 6: Increase post-connect wait and keep BLE alive

**Rationale:** iOS waits 15s after the second `getDeviceConfig` for "WiFi-dense environments". During this time BLE is still active. Our current 15s wait is before WiFi connect (wrong position).

**Implementation:**
- Move the 15s wait to AFTER `netsh wlan connect`
- During this wait, send a GetWifiIP poll every 5s to keep BLE alive and confirm AP is still running
- After 15s, check interface state

---

## Revised Complete Sequence

```
1. EnterTransferMode (0x41, [02 01 04]) → get SSID + password
2. PollWifiIpReadyAsync (0x41, [02 03]) × 10 retries → get IP
3. GetDeviceConfig (0x47, []) → confirm device in transfer mode    ← NEW
4. Wait 5 seconds                                                   ← NEW
5. Confirm GetWifiIP again (0x41, [02 03])                         ← NEW
6. netsh wlan add profile + connect (WPA2PSK/AES, nonBroadcast=true)
7. GetDeviceConfig (0x47, []) → keep glasses alive                 ← NEW
8. Wait 15 seconds, poll GetWifiIP every 5s during wait            ← MOVED
9. Check interface state → verify association
10. WaitForDhcpIp → get local IP
11. Probe http://192.168.31.1/files/media.config
```

---

## Step 7 (if all above fails): Manual connect experiment

**Rationale:** If the automated sequence still fails, we need to rule out a Windows driver/adapter issue.

**Procedure:**
1. Run test up to step 5 (confirm AP broadcasting via BLE)
2. Add a 60-second pause with console message "CONNECT MANUALLY NOW"
3. User manually connects to the glasses WiFi via Windows Settings
4. If manual connect works → problem is in our netsh profile/timing
5. If manual connect also fails → problem is AP compatibility with the Windows adapter

---

## Step 8 (if manual also fails): Scan for actual security type

**Rationale:** We need to know exactly what security the AP broadcasts.

**Procedure:**
1. During the 60-second pause, run:
   ```
   netsh wlan show networks mode=bssid
   ```
2. Look for the glasses SSID and note:
   - Authentication type (WPA2-Personal vs WPA-Personal vs Open)
   - Encryption type (CCMP/AES vs TKIP)
   - Channel / frequency
   - Signal strength

This definitively answers the security question.

---

## Priority Order

1. **Steps 1-3** (add getDeviceConfig + confirm IP) — highest priority, most likely root cause
2. **Step 4** (nonBroadcast=true) — essential for hidden networks
3. **Steps 5-6** (second getDeviceConfig + repositioned wait) — reliability
4. **Step 7** (manual test) — diagnostic if above fails
5. **Step 8** (scan security) — last resort diagnostic
