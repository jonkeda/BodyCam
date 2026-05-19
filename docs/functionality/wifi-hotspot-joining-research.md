# Windows WiFi Hotspot Joining — Research Findings

**Date:** 2026-05-17  
**Phase:** m36 Phase 5 — WiFi Hotspot Joining  
**Depends on:** Phase 2 (BLE session — complete), Phase 6 (WiFi Direct — in progress)

---

## 1. Executive Summary

The HeyCyan glasses support **two mutually exclusive WiFi modes** for file
transfer, chosen by the client platform:

| Platform | WiFi mode | SSID/password source | IP discovery |
|----------|-----------|----------------------|--------------|
| **Android** | WiFi Direct (P2P) | Auto-negotiated, no credentials | `WifiP2pInfo.groupOwnerAddress` |
| **iOS** | Standard hotspot | BLE callback from `openWifiWithMode` | Gateway probing (192.168.43.1, etc.) |
| **Windows** | ??? | ??? | ??? |

The glasses send the **same BLE command** (`{0x02, 0x01, 0x04}`) on both
platforms, but the connection method differs. On Windows, **neither approach
works yet**:

- **WiFi Direct**: `DeviceWatcher` finds **zero peers** (likely an
  unpackaged-app capability issue).
- **Standard WiFi scan**: No glasses SSID appears in regular WiFi scans.
- **BLE 0x08 IP notify**: Times out after 30 seconds, then arrives late
  (~60–90s) with an **invalid IP** (`1.11.0.0`).

---

## 2. Platform-by-Platform Analysis

### 2.1 Android — WiFi Direct (P2P)

Source: `CyanBridge/MainActivity.kt` + `WifiP2pManagerSingleton.kt`

**Flow:**

```
1. Register BLE notify listener (cmdType=2)
2. Initialize WifiP2pManagerSingleton → WifiP2pManager.initialize()
3. Register P2P BroadcastReceiver (STATE/PEERS/CONNECTION/THIS_DEVICE changed)
4. Start peer discovery: wifiP2pManager.discoverPeers() [16s timeout]
5. Send BLE command: glassesControl({0x02, 0x01, 0x04})
6. WIFI_P2P_PEERS_CHANGED → filter peers via isLikelyGlassesPeer()
   - Match by BLE MAC (most reliable)
   - Match by name prefix: AIM, AIMB-, GLASS
   - Match by 12-char hex pattern [A-F0-9]{12}
7. connectToDevice() with WPS PBC (wps.setup = 0)
8. WIFI_P2P_CONNECTION_CHANGED → requestConnectionInfo()
9. onConnectionInfoAvailable: groupOwnerAddress = glasses IP
10. BLE 0x08 notify arrives (bonus, after P2P connects)
11. bindProcessToNetwork() for HTTP traffic
12. HTTP: GET /files/media.config → GET /files/{filename}
```

**Key details:**
- Discovery timeout: 16 seconds
- Connect timeout: 5 seconds
- On discovery failure: `discoverPeersStable()` (stop + restart)
- P2P error recovery: `resetDeviceP2p()` sends BLE `{0x02, 0x01, 0x0F}`
- Network binding: `ConnectivityManager.bindProcessToNetwork()` ensures
  HTTP traffic goes over the P2P interface, not cellular/home WiFi

### 2.2 iOS — Standard WiFi Hotspot

Source: `QCSDKDemo/GlassesWiFiHandler.m` + `GlassesMediaDownloader.m`

**Flow:**

```
1. QCSDKCmdCreator openWifiWithMode:QCOperatorDeviceModeTransfer
   → success callback returns (ssid, password)
2. Override password with "123456789" (glasses return wrong one)
3. NEHotspotConfiguration(ssid, password, isWEP:NO)
   → configuration.joinOnce = YES
4. [NEHotspotConfigurationManager applyConfiguration:]
5. Wait 5–15 seconds for iOS to establish connection
6. Verify WiFi SSID via CNCopyCurrentNetworkInfo
7. Probe gateway IPs concurrently:
   - 192.168.43.1 (Android hotspot default)
   - 192.168.4.1
   - 192.168.1.1
   - 192.168.0.1
   - 10.0.0.1
8. HTTP: GET /files/media.config → GET /files/{filename}
```

**Key details:**
- The SSID and password come from the BLE response to `openWifiWithMode`
- The iOS SDK **always overrides the password** to `"123456789"` because the
  glasses return incorrect credentials
- `joinOnce = YES` means iOS auto-disconnects when the app stops using it
- IP discovery uses concurrent HTTP probes to multiple gateway candidates
- No WiFi Direct involved — iOS uses `NEHotspotConfiguration`
  (standard WPA2 hotspot)

### 2.3 Windows — Current State

**What works:**
- BLE connection and command sending ✓
- BLE command `{0x02, 0x01, 0x04}` (EnterTransferMode) succeeds ✓
- Battery notifications received ✓
- HTTP transfer protocol ready ✓

**What fails:**

#### WiFi Direct (primary strategy)

```csharp
var selector = WiFiDirectDevice.GetDeviceSelector(WiFiDirectDeviceSelectorType.AssociationEndpoint);
var watcher = DeviceInformation.CreateWatcher(selector, ...);
watcher.Start();
// → watcher.Added NEVER fires
// → 45s timeout → 0 peers found
```

**Probable cause:** The `dotnet test` runner is an unpackaged console
application. The `<DeviceCapability Name="wifiDirect" />` in
`Package.appxmanifest` only applies to packaged MSIX apps with an app
identity. Without app identity, the WiFi Direct API may silently fail to
enumerate peers.

**Alternative cause:** The WiFi adapter driver may not support WiFi Direct
P2P. `WiFiDirectDevice.GetDeviceSelector(AssociationEndpoint)` requires
driver-level P2P support.

#### Regular WiFi Scan (fallback strategy)

```csharp
await adapter.ScanAsync();
// Visible SSIDs (15): [jobaboe, Ziggo3168718, Blacklabel-5G, ...]
// → No glasses SSID found
```

WiFi Direct peers are invisible to standard WiFi scans. This is expected —
the glasses create a P2P group, not a standard AP.

#### BLE 0x08 IP Notify (last resort)

The glasses eventually send a 0x08-related frame, but it arrives **after
all timeouts expire** (~60–90 seconds after the command):

```
[BLE-NOTIFY] (14B) BC-73-08-00-FA-C7-01-0B-00-00-00-00-00-01
```

**This frame does NOT match our parser** (see Section 4 below).

---

## 3. The SSID/Password Question

### Does the glasses return SSID/password to Windows?

**No — not via the command we send.**

The iOS SDK uses `QCSDKCmdCreator openWifiWithMode:` which is a
higher-level API in the proprietary QCSDK binary framework. It sends a
BLE command and parses the response to extract SSID + password. We don't
have access to QCSDK source code — only the Objective-C header:

```objc
+ (void)openWifiWithMode:(QCOperatorDeviceMode)mode
                 success:(void (^)(NSString *, NSString *))suc
                    fail:(void (^)(NSInteger))fail;
```

**Key question:** Does `openWifiWithMode:` send the same bytes
(`{0x02, 0x01, 0x04}`) as our `HeyCyanCommands.EnterTransferMode()`, or
does it send different bytes that trigger hotspot mode instead of WiFi
Direct mode?

The `QCOperatorDeviceMode` enum has a `Transfer` value that maps to mode
byte `0x04`. So the raw BLE bytes are likely the same. But the QCSDK
framework may parse a **different response frame** (not 0x08) to extract
the SSID/password.

### Observed BLE notifications after EnterTransferMode

During real hardware testing, these notifications were observed:

| Time | Frame | Parsed type (byte[6]) |
|------|-------|-----------------------|
| T+0 | `BC-73-03-00-5C-51-05-51-00` (9B) | 0x05 (Battery, 81%) |
| T+60–90s | `BC-73-08-00-FA-C7-01-0B-00-00-00-00-00-01` (14B) | 0x01 (Unknown) |

**No SSID/password frame was received.** The glasses may need a
different command or a different BLE characteristic to return hotspot
credentials.

---

## 4. BLE Frame Format Analysis

### Standard frame layout

```
[0-1]  Magic:          BC-73 (constant)
[2]    Payload length: Number of bytes from [6] onwards
[3]    Flags/reserved: Usually 0x00
[4-5]  Counter/status: Varies per frame
[6]    Notify type:    0x02=button, 0x03=voice, 0x05=battery, 0x08=IP, 0x09=P2P-error
[7..]  Payload:        Type-specific data
```

### Battery frame (9 bytes, type 0x05)

```
BC-73-03-00-5C-51-05-51-00
 ^^   ^^         ^^ ^^  ^^
 magic len       ty pct chg
```

- Payload length: 3 bytes ([6]=type, [7]=percentage, [8]=charging)
- type = 0x05, percentage = 0x51 (81%), charging = 0x00 (no)

### The 14-byte frame — **CRITICAL FINDING**

```
BC-73-08-00-FA-C7-01-0B-00-00-00-00-00-01
[0]  [1]  [2]  [3]  [4]  [5]  [6]  [7]  [8]  [9]  [10] [11] [12] [13]
magic      len  flg  counter   type
```

- **byte[2] = 0x08** → payload length = 8 bytes (frame[6..13])
- **byte[6] = 0x01** → notify type is **0x01, NOT 0x08**

**This means:**
1. Our `TryParseTransferIp()` checks `frame[6] == 0x08` → **DOES NOT MATCH**
2. Our `WaitForNotifyAsync(0x08)` waits for `_pendingResponses[0x08]` →
   **NEVER COMPLETED** by this frame (type is 0x01)
3. The 0x08 value at byte[2] is the payload length, not the notify type
4. The conversation summary incorrectly identified this as a "0x08 IP notify"

**What is notify type 0x01?**

- Type 0x01 is **not handled** in our `OnCharacteristicValueChanged` switch
  (falls through to `default: Unknown notify type`)
- It may be a **mode change acknowledgment** — the glasses confirming
  they've entered transfer mode
- Payload bytes `0B-00-00-00-00-00-01` could encode mode status, WiFi
  readiness, or error state
- The CyanBridge Android code handles this differently — the
  `LargeDataHandler` may parse type 0x01 responses as mode confirmations

**Implication:** We have **never** received a true 0x08 IP notify frame
from the glasses. The glasses may not send 0x08 until a WiFi Direct peer
actually connects (which we've never achieved on Windows).

---

## 5. Why WiFi Direct Fails on Windows

### Hypothesis 1: Unpackaged app identity (most likely)

The `wifiDirect` device capability in `Package.appxmanifest` only applies
when the app runs as a packaged MSIX/AppX with an app identity. The
`dotnet test` runner and even `WindowsPackageType=None` MAUI builds
run as unpackaged processes. WinRT APIs that require capabilities may
silently return empty results or fail.

**Evidence:**
- `DeviceWatcher.Added` never fires — zero peers found
- No error or exception from the WinRT API (just silence)
- This matches documented WinRT behavior for capability-gated APIs

**Test needed:** Run the WiFi Direct discovery from a **packaged** MAUI app
(deployed via MSIX) where the manifest capabilities are enforced.

### Hypothesis 2: WiFi adapter driver limitation

Not all WiFi adapters support WiFi Direct. The adapter must support the
WiFi Direct (P2P) feature in its driver. Intel adapters generally do; some
Realtek adapters don't.

**Test needed:** Run `netsh wlan show drivers` and check for
"Wi-Fi Direct Device" or "Supported" in the output.

### Hypothesis 3: Wrong device selector type

We use `WiFiDirectDeviceSelectorType.AssociationEndpoint`. The alternative
`DeviceInterface` may work differently:

```csharp
// Current (not working):
WiFiDirectDevice.GetDeviceSelector(WiFiDirectDeviceSelectorType.AssociationEndpoint)

// Alternative to try:
WiFiDirectDevice.GetDeviceSelector(WiFiDirectDeviceSelectorType.DeviceInterface)
```

### Hypothesis 4: Timing issue

Android starts peer discovery **before** sending the BLE command. We send
the BLE command first, then start discovery. The glasses' P2P group may
have a short advertisement window that we miss.

---

## 6. Alternative Approaches

### 6.1 Try iOS-style hotspot joining

Instead of WiFi Direct, try the iOS approach:

1. Send a different BLE command that triggers hotspot mode (if one exists)
2. Or wait for the glasses to broadcast a standard SSID after receiving
   `{0x02, 0x01, 0x04}`
3. Use `netsh wlan connect` or `WiFiAdapter.ConnectAsync()` to join

**Problem:** We've never seen a glasses SSID in WiFi scans. The glasses
may only support hotspot mode on iOS (where the QCSDK framework sends
a platform-specific variant of the command).

### 6.2 Parse type 0x01 notification

The 14-byte frame with type 0x01 may contain useful information:

```
Payload: 0B-00-00-00-00-00-01
```

- Could be a mode-change response with status codes
- Could contain a WiFi channel or frequency hint
- Need to reverse-engineer by comparing with CyanBridge `LargeDataHandler`

### 6.3 Use `netsh wlan connect` with known SSID pattern

If the glasses DO create a standard hotspot (just not found by our scan),
try connecting by SSID pattern:

```powershell
netsh wlan show networks
netsh wlan connect name="<glasses-ssid>" ssid="<glasses-ssid>"
```

SSID patterns to try: device name, `DIRECT-{mac}`, `QC{model}`, `O_{mac}`.

### 6.4 Increase BLE notify timeout

If the glasses send a true 0x08 IP frame after a longer delay (>90s),
increasing the timeout from 30s to 120s might capture it. But the 14-byte
frame we observed was type 0x01, not 0x08 — so this alone won't help.

### 6.5 Send ResetP2P before EnterTransferMode

Android calls `resetDeviceP2p()` (`{0x02, 0x01, 0x0F}`) to reset the
glasses' P2P state machine before starting a new transfer. This might help
if the glasses are stuck in a previous P2P state:

```csharp
await SendCommandAsync(HeyCyanCommands.ResetP2p(), ct);
await Task.Delay(2000, ct);
await SendCommandAsync(HeyCyanCommands.EnterTransferMode(), ct);
```

### 6.6 Run from packaged MAUI app

Deploy the BodyCam app as a packaged MSIX to test WiFi Direct with proper
manifest capabilities. This is the most likely fix if Hypothesis 1 is
correct:

```xml
<!-- Package.appxmanifest -->
<DeviceCapability Name="wifiDirect" />
<DeviceCapability Name="bluetooth" />
<DeviceCapability Name="wifiControl" />
```

### 6.7 WiFi Direct Advertisement Publisher (role reversal)

Instead of discovering the glasses as a WiFi Direct peer, act as a WiFi
Direct **advertiser** and let the glasses connect to us:

```csharp
var publisher = new WiFiDirectAdvertisementPublisher();
publisher.Advertisement.IsAutonomousGroupOwnerEnabled = true;
publisher.StatusChanged += OnPublisherStatusChanged;
publisher.Start();
```

This may work if the glasses' firmware expects the connected device to
be the group owner.

---

## 7. What the Android CyanBridge Does Differently

Key differences between Android `WifiP2pManager` and our Windows approach:

| Aspect | Android | Windows |
|--------|---------|---------|
| API | `WifiP2pManager` (system service) | `WiFiDirectDevice` (WinRT) |
| Discovery | `discoverPeers()` with broadcast receiver | `DeviceWatcher` with event handlers |
| Connection | `connect()` with `WifiP2pConfig(WPS PBC)` | `FromIdAsync()` |
| Network binding | `bindProcessToNetwork()` explicit | None (assumed automatic) |
| Error recovery | `resetDeviceP2p()` BLE command | Not implemented |
| Discovery timing | Before BLE command | After BLE command |
| Retry logic | `discoverPeersStable()` stop+restart | None |
| Timeout | 16s discovery, 5s connect | 45s combined |

**Notable:** Android starts P2P discovery BEFORE sending the BLE
transfer command. We do it the other way around. This ordering difference
may be significant — the glasses' P2P advertisement might be short-lived.

---

## 8. Recommended Next Steps (Priority Order)

### P0 — Quick diagnostics

1. **Check WiFi Direct driver support:**
   ```powershell
   netsh wlan show drivers | Select-String -Pattern "Wi-Fi Direct|Hosted network"
   ```

2. **Parse the type 0x01 notification:** Add handling in
   `OnCharacteristicValueChanged` for type 0x01 to log the payload
   structure. This frame likely contains transfer mode status.

3. **Register a pending waiter for type 0x01** (not just 0x08) after
   sending the transfer mode command.

### P1 — Fix discovery ordering

4. **Start WiFi Direct discovery BEFORE sending the BLE command** (match
   Android flow). The glasses may only advertise briefly.

5. **Send ResetP2P (`{0x02, 0x01, 0x0F}`)** before EnterTransferMode to
   clear stale P2P state.

### P2 — Packaged app test

6. **Deploy as packaged MSIX** and test WiFi Direct with proper
   capabilities. This is the definitive test for Hypothesis 1.

### P3 — Alternative WiFi mode

7. **Investigate whether the glasses support hotspot mode on Windows.**
   The iOS SDK gets SSID/password from the BLE response. If we can
   reverse-engineer which response frame carries the SSID, we could
   use standard `WiFiAdapter.ConnectAsync()` instead of WiFi Direct.

---

## 9. Open Questions

1. **Does `openWifiWithMode:` send different bytes than `{0x02, 0x01, 0x04}`?**
   We can't verify without QCSDK source. The Objective-C header only shows
   the public API.

2. **What does notify type 0x01 mean?** Our handler drops it as "unknown".
   It may be the mode-change acknowledgment that iOS parses for SSID.

3. **Do the glasses support both hotspot AND WiFi Direct simultaneously?**
   Or is it one or the other based on the connecting platform?

4. **Is the "123456789" password universal?** iOS always uses it. Would it
   work on Windows if we could find the SSID?

5. **Does `WiFiDirectDevice.GetDeviceSelector()` work from unpackaged apps?**
   Microsoft's documentation is ambiguous on capability requirements for
   desktop apps.

---

## 10. Appendix — Raw Test Output

### BLE command trace

```
[BLE-WRITE]  (2B) 02-04                              ← GetBattery
[BLE-WRITE]  (5B) 03-8D-D3-09-6A                     ← SyncTime
[BLE-WRITE]  (3B) 02-01-04                            ← EnterTransferMode
```

### BLE notification trace

```
[BLE-NOTIFY] (9B)  BC-73-03-00-5C-51-05-51-00        ← Battery (type=0x05, 81%)
[BLE-NOTIFY] (14B) BC-73-08-00-FA-C7-01-0B-00-00-00-00-00-01  ← Unknown (type=0x01, late ~60-90s)
```

### WiFi Direct discovery

```
[WIFIDIRECT] Starting peer discovery...
[WIFIDIRECT] Failed: A task was canceled.             ← 45s timeout, 0 peers found
```

### Regular WiFi scan

```
[WIFI] No glasses hotspot found. Visible SSIDs (15):
       [jobaboe, Ziggo3168718, Blacklabel-5G, ...]    ← Only household networks
```
