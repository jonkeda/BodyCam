# WiFi Direct Transfer — Protocol Findings

**Date:** 2026-05-17  
**Phase:** m36 Phase 6 — WiFi Photo Transfer

## Summary

The HeyCyan glasses use **WiFi Direct (P2P)**, not a regular WiFi hotspot,
for file transfer. This was discovered through BLE protocol tracing during
real-hardware testing on Windows.

## Evidence

### BLE trace (raw GATT notifications)

```
[BLE-WRITE]  (2B) 02-04                              ← GetBattery
[BLE-WRITE]  (5B) 03-8D-D3-09-6A                     ← SyncTime
[BLE-WRITE]  (3B) 02-01-04                            ← EnterTransferMode ✓
[BLE-NOTIFY] (9B) BC-73-03-00-5E-61-05-56-00          ← Battery (type 0x05, 86%)
```

After sending `EnterTransferMode`, the glasses respond with a battery
update only — no 0x08 IP notification within 30 seconds.

### WiFi scan after transfer mode command

```
Visible SSIDs (15): [jobaboe, Ziggo3168718, Blacklabel-5G, ...]
```

**No glasses SSID appears** in regular WiFi scans. The glasses use WiFi
Direct, which is invisible to standard `WiFiAdapter.ScanAsync()`.

## Protocol comparison by platform

### Android (CyanBridge reference)

1. Register BLE notify listener (`cmdType=2`)
2. Initialize `WifiP2pManager` + register broadcast receiver
3. Start P2P peer discovery: `wifiP2pManager.discoverPeers()`
4. Send BLE command: `glassesControl({0x02, 0x01, 0x04})`
5. On `WIFI_P2P_PEERS_CHANGED`: filter peers via `isLikelyGlassesPeer()`,
   connect via `connectToDevice()` with WPS PBC
6. On `WIFI_P2P_CONNECTION_CHANGED` (connected): extract
   `info.groupOwnerAddress` as candidate IP
7. BLE 0x08 notify arrives with glasses IP (only after P2P connects)
8. Try candidate IPs: BLE IP, group owner IP, subnet guesses
9. HTTP: `GET /files/media.config` then `GET /files/{name}`

### iOS (QCSDKDemo reference)

1. `[QCSDKCmdCreator openWifiWithMode:QCOperatorDeviceModeTransfer]`
   → success callback returns SSID + password
2. `NEHotspotConfiguration(ssid, password ?? "123456789")`
3. Wait for iOS to join (up to 90s with retries)
4. Probe common gateway IPs: `192.168.43.1`, `192.168.4.1`, etc.
5. HTTP: same `/files/media.config` + `/files/{name}`

### Windows (this implementation)

Windows has no `WifiP2pManager` equivalent. Uses WinRT
`Windows.Devices.WiFiDirect.WiFiDirectDevice` API:

1. Send BLE command: `{0x02, 0x01, 0x04}`
2. Start WiFi Direct device watcher for P2P peers
3. Match glasses peer by device name (MAC-like patterns, "AIM", "GLASS")
4. Connect via `WiFiDirectDevice.FromIdAsync()`
5. Extract remote IP from `GetConnectionEndpointPairs()`
6. Optionally wait for BLE 0x08 notify (may arrive after P2P connects)
7. HTTP: `GET /files/media.config` then `GET /files/{name}`

## Key protocol details

### Peer identification (Android reference)

```kotlin
fun isLikelyGlassesPeer(device: WifiP2pDevice, bleMacNoColon: String?): Boolean {
    val name = device.deviceName.uppercase()
    if (name.isBlank()) return false
    if (!bleMacNoColon.isNullOrBlank() && name.contains(bleMacNoColon)) return true
    if (name.startsWith("AIM") || name.contains("AIMB-") || name.contains("GLASS")) return true
    return Regex("[A-F0-9]{12}").containsMatchIn(name)
}
```

### IP resolution strategy

The Android app maintains multiple candidate IPs and probes all:

| Source | Priority |
|--------|----------|
| BLE 0x08 notify IP | 1 (best, may not arrive on Windows) |
| WiFi Direct group owner address | 2 |
| Subnet guess `{prefix}1` | 3 |
| Subnet guess `{prefix}79` | 4 |
| Full subnet scan `/24` | last resort |

### BLE commands

| Command | Bytes | Purpose |
|---------|-------|---------|
| Enter transfer mode | `02 01 04` | Glasses bring up WiFi Direct |
| Exit transfer mode | `02 01 09` | Glasses tear down WiFi Direct |
| Reset P2P | `02 01 0F` | Reset glasses P2P state machine |

### HTTP endpoints

| Endpoint | Method | Returns |
|----------|--------|---------|
| `/files/media.config` | GET | Text manifest, one filename per line |
| `/files/{filename}` | GET | Raw file bytes |

## Implications for implementation

1. **`WindowsGlassesWiFiManager` is insufficient** — regular WiFi scanning
   cannot find WiFi Direct peers. Need a new WiFi Direct manager.

2. **`EnterTransferModeAsync` flow must change:**
   - Send BLE command
   - Start WiFi Direct peer discovery (concurrently)
   - Match and connect to glasses peer
   - Extract IP from endpoint pairs or BLE notify
   - Then proceed with HTTP

3. **Manifest capability needed:** `<DeviceCapability Name="wifiDirect" />`
   in `Package.appxmanifest` (already have `bluetooth` and `wifiControl`).

4. **The existing `HeyCyanMediaTransfer` / HTTP layer is correct** — once
   we have the IP, the HTTP protocol is identical across platforms.
