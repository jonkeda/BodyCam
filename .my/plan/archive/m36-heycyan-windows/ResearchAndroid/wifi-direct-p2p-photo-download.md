# WiFi Direct P2P Photo Download — Android Research & Windows Implementation Plan

**Date:** 2026-05-19  
**Goal:** Understand Android's WiFi Direct P2P photo transfer and implement it on Windows  

---

## 1. How Android (CyanBridge) Downloads Photos

### Overview

Android uses **WiFi Direct (P2P)** to transfer files. The phone acts as a P2P client, the glasses form a WiFi Direct group (acting as group owner with an embedded HTTP server). The entire flow is orchestrated via BLE control commands + WiFi P2P APIs.

### Step-by-Step Flow

```
Phone                                    Glasses
  |                                          |
  |-- WiFi P2P: discoverPeers() ----------->|  (start P2P scanning)
  |                                          |
  |-- BLE: glassesControl(0x02,0x01,0x04) ->|  (enter transfer mode)
  |                                          |  (glasses start WiFi Direct group)
  |<-- BLE Notify 0x41: payload[0]==0x01 ---|  (mode-change ACK)
  |                                          |
  |<-- WIFI_P2P_PEERS_CHANGED broadcast ----|
  |   (filter: isLikelyGlassesPeer())       |
  |                                          |
  |-- WiFi P2P: connectToDevice(peer, PBC)->|  (WPS Push Button Config)
  |                                          |
  |<-- WIFI_P2P_CONNECTION_CHANGED ---------|  (connected)
  |   Extract: WifiP2pInfo.groupOwnerAddress |
  |                                          |
  |-- BLE: getDeviceConfig(0x47, empty) --->|  (CRITICAL: activates AP radio)
  |                                          |
  |-- BLE: poll GetWifiIP(0x02,0x03) ×10 ->|  (retry up to 10 times)
  |<-- BLE Notify 0x41: payload[0]==0x08 ---|  (IP in bytes [1..4])
  |                                          |
  |-- Bind process to P2P network ----------|
  |   ConnectivityManager.bindProcessToNetwork()
  |                                          |
  |-- HTTP: GET /files/media.config ------->|  (plaintext file listing)
  |<-- "photo1.jpg, 1234567, 1715000000, Photo\n..." |
  |                                          |
  |-- HTTP: GET /files/photo1.jpg --------->|  (binary download)
  |<-- JPEG bytes -------------------------|
  |                                          |
  |-- BLE: glassesControl(0x02,0x01,0x09)->|  (exit transfer mode)
  |-- WiFi P2P: disconnect() --------------|
```

### Key Android APIs Used

| API | Purpose |
|-----|---------|
| `WifiP2pManager.discoverPeers()` | Scan for WiFi Direct peers |
| `WifiP2pManager.connect()` | Connect to a peer using WPS PBC |
| `WifiP2pBroadcastReceiver` | Receive `WIFI_P2P_PEERS_CHANGED`, `WIFI_P2P_CONNECTION_CHANGED` |
| `WifiP2pInfo.groupOwnerAddress` | Get the group owner IP after connection |
| `ConnectivityManager.bindProcessToNetwork()` | Route HTTP traffic over P2P network |

### Peer Identification Heuristic

```kotlin
fun isLikelyGlassesPeer(device: WifiP2pDevice, bleMacNoColon: String?): Boolean {
    val name = device.deviceName.uppercase()
    if (name.isBlank()) return false
    if (!bleMacNoColon.isNullOrBlank() && name.contains(bleMacNoColon)) return true
    if (name.startsWith("AIM") || name.contains("AIMB-") || name.contains("GLASS")) return true
    if (Regex("[A-F0-9]{12}").containsMatchIn(name)) return true
    return false
}
```

### BLE Protocol Details

| Command | Bytes | Purpose |
|---------|-------|---------|
| Enter transfer mode | `02 01 04` | Glasses start WiFi Direct group |
| Exit transfer mode | `02 01 09` | Glasses tear down WiFi Direct |
| Reset P2P | `02 01 0F` | Reset glasses P2P state machine |
| Get WiFi IP | `02 03` | Poll for IP readiness |
| Device Config | `02 47` (empty payload) | **CRITICAL** — activate AP radio |

### BLE Notify Frames (type 0x41)

| payload[0] | Meaning |
|------------|---------|
| `0x01` | Mode-change ACK (first response after EnterTransferMode) |
| `0x08` | WiFi IP ready — `payload[1..4]` = IPv4 octets |
| `0x09` | P2P/WiFi error (`0xFF` = common, non-fatal) |

### HTTP Transfer Protocol

| Endpoint | Method | Returns |
|----------|--------|---------|
| `/files/media.config` | GET | Plaintext: `filename, size, timestamp, Type` per line |
| `/files/{filename}` | GET | Raw binary (JPEG, MP4, Opus) |

---

## 2. The Windows Problem

### No Native WiFi Direct P2P Client API

Windows has `Windows.Devices.WiFiDirect.WiFiDirectDevice` (WinRT), which provides:
- ✅ Device watcher / peer discovery
- ✅ `WiFiDirectDevice.FromIdAsync()` to connect
- ✅ Endpoint pair extraction for IP
- ✅ Pairing with PIN

**But it does NOT work** for our use case. After extensive testing (see phase-8-wifi-photos RCAs 801-816), the Windows WiFi Direct implementation has these problems:

1. **Peer discovery never finds the glasses** — the `DeviceWatcher` with `WiFiDirectDevice.GetDeviceSelector()` finds zero peers, even though Android finds them immediately
2. **Missing `getDeviceConfig` (0x47) command** — the glasses firmware needs this BLE command to actually activate the WiFi radio. Windows never sends it
3. **Single-shot BLE notify listener** — consumes the mode-change ACK (payload[0]==0x01) and misses the IP notification (payload[0]==0x08)
4. **No `bindProcessToNetwork` equivalent** — Windows doesn't have a way to route traffic specifically over a WiFi Direct interface (Android requires this because the phone's default route is cellular/home WiFi)

### What We've Tried

- WinRT `WiFiDirectDevice` peer discovery → zero peers found
- Regular WiFi scanning for glasses SSID → not visible (WiFi Direct ≠ regular WiFi)
- Various timing adjustments (15s, 30s, 45s waits) → no change
- The glasses AP **appeared once** during a debugging session with ~50s of continuous BLE polling, suggesting timing/keepalive issues

---

## 3. Research: WiFi Framework .NET Edition (btframework.com)

### What Is It?

A commercial SDK from Soft Service Company (btframework.com) that wraps Windows WLAN and WiFi Direct APIs into a managed .NET library. Available as:
- `.NET Edition` — C# classes for .NET Framework, .NET Standard, .NET Core, .NET (modern), .NET MAUI
- `C++ Edition` — static C++ library
- `VCL Edition` — Delphi/Lazarus

**Latest version:** 7.12.11.0 (released 2026-03-10)  
**Not on NuGet** — distributed as installer or 7z archive from btframework.com

### WiFi Direct Features

| Feature | Supported |
|---------|-----------|
| WiFi Direct legacy SoftAP (host an AP) | ✅ |
| WiFi Direct SoftAP Black/White lists | ✅ |
| WiFi Direct advertiser | ✅ |
| WiFi Direct devices watcher | ✅ |
| **WiFi Direct client** | ✅ |
| Enumerate paired WiFi Direct devices | ✅ |
| Mobile Hotspot control | ✅ |
| Get local and remote IPs for peers | ✅ |
| Network connection monitoring | ✅ |
| IP settings read/change | ✅ |

### Key Classes

| Class | Purpose |
|-------|---------|
| `wclWiFiSoftAp` | Host a WiFi Direct SoftAP (not what we need) |
| `wclWiFiDirectClient` | **Connect to a WiFi Direct peer** (this is what we need) |
| `wclWiFiDirectDeviceWatcher` | Discover WiFi Direct devices |
| `wclMobileHotspot` | Control Windows Mobile Hotspot |

### Pricing

| License | Price (USD) |
|---------|-------------|
| Individual (no source) | $125 |
| Single Developer | $250 |
| Single Developer + Source | $500 |
| Build Server | $1,000 |
| Site | $2,500 |

### Evaluation

**Pros:**
- Has a `wclWiFiDirectClient` class — exactly what we need for P2P client role
- Handles peer discovery, connection, and IP extraction
- Active development (last update 2026-03)
- Supports .NET MAUI (our framework)
- Abstracts the WinRT complexity that's failing in our current implementation
- May handle the edge cases in Windows WiFi Direct that our WinRT code doesn't

**Cons:**
- **Not on NuGet** — manual dependency management
- Commercial license required ($125-$500)
- Proprietary, closed-source (unless you buy $500 tier)
- Demo version shows "Unregistered" dialog + cannot ship in production
- **Unknown if it solves the core problem** — if Windows WiFi Direct drivers don't support P2P client role for our adapter, no SDK can fix that
- Adds an external dependency for a single feature

### Verdict

**Worth evaluating with the demo version.** The demo will quickly answer whether `wclWiFiDirectClient` can discover and connect to the glasses as a P2P client. If it works where WinRT fails, it's worth the $125-250 license. If it fails the same way, the problem is at the driver/adapter level, not the API level.

---

## 4. Alternative Approaches (If WiFi Direct Fails)

### Option A: Hotspot Mode (iOS Approach)

The glasses also support regular WiFi hotspot mode (used by iOS SDK). This bypasses WiFi Direct entirely:

1. Send BLE transfer command + `getDeviceConfig(0x47)` 
2. Glasses broadcast a standard WiFi SSID with password `123456789`
3. Windows joins the SSID using `WiFiAdapter.ConnectAsync()` with a WLAN profile
4. Probe candidate IPs: `192.168.43.1`, `192.168.4.1`, `192.168.1.1`
5. HTTP download from the one that responds

**Status:** Partially implemented but AP never becomes visible (likely because `0x47` command is missing).

### Option B: USB/ADB Bridge (Last Resort)

Use an Android phone as a bridge — phone connects via P2P, then ADB forwards HTTP port to PC. Not practical for production but useful for testing.

### Option C: Windows WiFi Direct with Fixed BLE Flow

Before adding any SDK, fix the BLE protocol issues first:
1. Send `getDeviceConfig(0x47)` to activate AP radio
2. Capture ALL 0x41 notifications (channel-based, not single-shot)
3. Wait for `payload[0] == 0x08` specifically
4. Keep WiFi Direct discovery running in parallel

This is the cheapest approach and may be all that's needed.

---

## 5. Implementation Plan

### Phase 1: Fix BLE Protocol (No New Dependencies)

**Priority: HIGHEST — do this first**

1. **Add `getDeviceConfig` (0x47) command** to BLE command set
   - Send before WiFi connect attempt
   - Send again after WiFi connect (iOS does this twice)
2. **Replace single-shot notify waiter** with channel-based capture
   - Use `Channel<byte[]>` for all 0x41 notifications during transfer
   - Wait specifically for `payload[0] == 0x08` (IP notification)
   - Handle `payload[0] == 0x01` (ACK) and `payload[0] == 0x09` (error) separately
3. **Fix timing**
   - 5-second wait after first `getDeviceConfig` (let AP radio activate)
   - Keep WiFi Direct discovery running during the entire sequence
   - 15-second wait after WiFi connect (not before)

### Phase 2: Evaluate WiFi Framework Demo

**Priority: HIGH — run in parallel with Phase 1 testing**

1. Download WiFi Framework .NET Edition demo from btframework.com
2. Create a standalone test app (not in main project)
3. Test `wclWiFiDirectDeviceWatcher` — does it discover the glasses?
4. Test `wclWiFiDirectClient` — can it connect as P2P client?
5. Compare discovered peer list with WinRT `DeviceWatcher` results
6. Document findings: same result as WinRT, or better?

### Phase 3: Implement Working Solution

**Depends on Phase 1 + 2 results:**

| Phase 1 Result | Phase 2 Result | Action |
|----------------|----------------|--------|
| BLE fix works, WinRT finds peer | N/A | Ship with WinRT (no new dependency) |
| BLE fix works, WinRT still fails | WiFi Framework finds peer | Integrate WiFi Framework |
| BLE fix works, both fail to find peer | N/A | Fall back to hotspot mode (Option A) |
| BLE fix fails (no 0x08 notify) | N/A | Deeper firmware investigation needed |

### Phase 4: Hotspot Fallback (If P2P Fails Entirely)

1. After BLE fix, attempt hotspot join as parallel strategy
2. Scan for glasses SSID patterns: `QC*`, `O_*`, `M01*`, `*Cyan*`, `DIRECT-*`
3. Join with password `123456789` using `WiFiAdapter.ConnectAsync()`
4. Probe candidate gateway IPs
5. HTTP download from responding IP

### Phase 5: End-to-End Integration

1. Unified `TransferPhotosAsync()` that tries P2P first, then hotspot fallback
2. Progress reporting for multi-file downloads
3. Error recovery (BLE disconnect, WiFi timeout, partial downloads)
4. Integration tests with real hardware

---

## 6. Open Questions

1. **Does the glasses firmware actually support P2P client connections from Windows?** — Android uses `WifiP2pManager` which is Linux-kernel-based; Windows WiFi Direct may use incompatible WPS negotiation
2. **Is the `0x47` DeviceConfig command the missing key?** — iOS labels it "CRITICAL VERIFICATION STEP" but exact firmware behavior is unknown
3. **Does WiFi Framework use different Windows APIs than WinRT?** — If it wraps the same `WiFiDirectDevice`, it won't help. If it uses lower-level WLAN APIs, it might
4. **Can the glasses do both P2P and hotspot simultaneously?** — If not, we may need to send a different BLE command variant to force hotspot mode on Windows
5. **What WiFi adapter is required?** — Some Windows WiFi adapters don't support WiFi Direct client role (only SoftAP host)
