# RCA-810: WiFi Photo Transfer — What's Actually Needed

**Date:** 2025-05-18  
**Status:** OPEN — next steps defined  
**Severity:** High (photo download blocked)

## Current State

BLE protocol is fully working (RCA-809). We can:
- Connect to glasses ✓
- Query battery ✓  
- Take a photo via BLE command ✓ (response `BC-41-03-00-60-57-01-01-0B`)
- Enter transfer mode ✓ (response `BC-41-0B-00-C6-AD-01-04-12-00-00-00-00-00-01-00-00`)

What does NOT work: obtaining the glasses IP to download photos over HTTP.

## What the SDKs Actually Do

### iOS SDK (WIFI_TRANSFER_ARCHITECTURE.md)

1. Send `openWifiWithMode:QCOperatorDeviceModeTransfer`
2. BLE notify callback delivers **SSID + password** as strings
3. Join the glasses WiFi hotspot using `NEHotspotConfigurationManager`
4. Probe candidate IPs: `192.168.43.1`, `192.168.4.1`, `192.168.1.1`, `192.168.0.1`, `10.0.0.1`
5. Fetch `http://<ip>/manifest.json`

### Android SDK (AGENTS.md)

1. Start WiFi P2P discovery (`WifiP2pManager.discoverPeers()`)
2. Send BLE command: `glassesControl(byteArrayOf(0x02, 0x01, 0x04))`
3. Listen for **multiple** BLE notify frames on action 0x41:
   - `payload[0] == 0x01`: mode-change ACK (first response)
   - `payload[0] == 0x08`: **glasses WiFi IP** in bytes `[1..4]` as IPv4
   - `payload[0] == 0x09`: P2P/WiFi error (`payload[1] == 0xFF` is common/noisy)
4. Bind process to P2P network
5. Fetch `http://<glasses-ip>/files/media.config` (plaintext file listing)
6. Download each file from `http://<glasses-ip>/files/<filename>`

### Key Difference

- **iOS**: glasses broadcast a WiFi **hotspot**; phone joins it
- **Android**: glasses join a WiFi **Direct P2P group**; phone is group owner

Both modes appear supported by the hardware. The firmware likely uses WiFi Direct when a P2P peer is available, and falls back to hotspot mode otherwise (or the mode may depend on the BLE command variant sent).

## Why Our Windows Code Fails

### Bug 1: Single-shot notify waiter consumes the wrong response

```csharp
var notifyTask41 = WaitForNotifyAsync(0x41, TimeSpan.FromSeconds(90), ct);
await SendCommandAsync(HeyCyanCommands.EnterTransferMode(), ct);
```

The first 0x41 response (`payload[0] == 0x01`, mode-change ACK) completes `notifyTask41`. We never see the **second** 0x41 notification with `payload[0] == 0x08` (IP address) because `_pendingResponses.TryRemove(action, out tcs)` fires on the ACK and no new waiter is registered.

### Bug 2: DispatchGlassesControl never re-registers a waiter

When `DispatchGlassesControl` receives `payload[0] == 0x01, payload[1] == 0x04`, it tries `_pendingResponses.TryRemove(0x41, out transferTcs)` — but that waiter was already consumed by the ACK. The 0x08 IP notify arrives later with no waiter at all.

### Bug 3: WiFi Direct timeout (unvalidated assumption)

The 45-second WiFi Direct timeout might be failing because:
- Corporate network blocks P2P discovery (plausible but unproven)
- MSIX packaging required for wifiDirect capability (unproven)
- Peer name doesn't match `IsLikelyGlassesPeer()` heuristic (no glasses peer was logged)
- Glasses don't advertise P2P until they see a discovery probe (possible)

### Bug 4: WiFi hotspot scan timing

The WiFi scan waits only ~12 seconds total (3s initial + 3 attempts × 3s). The glasses hotspot may take longer to appear, or requires a different BLE command variant to activate hotspot (vs WiFi Direct) mode.

## Fix Plan

### Step 1: Collect ALL 0x41 notifications (not just the first)

Replace the single-shot `WaitForNotifyAsync(0x41, ...)` with a channel/queue that captures all 0x41 payloads during transfer mode. Specifically wait for `payload[0] == 0x08` (IP notification).

```
Approach: use a Channel<byte[]> or register a callback on the 
OnCharacteristicValueChanged handler that feeds transfer-mode 
notifications into a dedicated queue.
```

### Step 2: Parse the 0x08 IP notification

Per Android AGENTS.md: `payload[0] == 0x08`, then `payload[1..4]` = IPv4 octets.

```csharp
if (payload[0] == 0x08 && payload.Length >= 5)
{
    var ip = new IPAddress(payload.AsSpan(1, 4));
    // ip is the glasses HTTP server address
}
```

### Step 3: Keep WiFi Direct discovery running in parallel

Don't fail fast on WiFi Direct. Keep the DeviceWatcher running while waiting for the BLE IP notification. If WiFi Direct connects first, use its endpoint IP. If BLE IP arrives first, use that.

### Step 4: Add hotspot join as parallel fallback

If the glasses also broadcast a hotspot SSID (iOS flow), the WiFi scan should keep retrying for longer (30+ seconds). The SSID patterns to match: `QC*`, `O_*`, `M01*`, `*Cyan*`, `DIRECT-*`.

### Step 5: Probe candidate IPs (iOS fallback)

If hotspot is joined but no IP from BLE, probe the iOS candidate list:
- `192.168.43.1` (Android hotspot default — glasses likely use this)
- `192.168.4.1`
- `192.168.1.1`

HTTP probe: `GET http://<ip>/files/media.config` — if it returns 200, that's the glasses.

## HTTP Transfer Protocol (confirmed from both SDKs)

Once IP is known:

| Endpoint | Returns |
|----------|---------|
| `GET /files/media.config` | Plaintext file listing, one filename per line |
| `GET /files/<filename>` | Binary file content (JPG, MP4, OPUS) |

Alternative (iOS): `GET /manifest.json` returns JSON with `{"files": [...]}`.

## Execution Order

1. **Fix the notify waiter** — capture 0x08 IP notification (highest impact, pure code fix)
2. **Run test** — see if 0x08 notify arrives with valid IP
3. **If IP received** — attempt HTTP download (media.config + file fetch)
4. **If no 0x08** — investigate WiFi Direct logging (log ALL peers found)
5. **If still blocked** — try hotspot join with longer timeout + IP probing

## Test Validation

Success = end-to-end: take photo → enter transfer → get IP → download JPG → verify JPEG magic bytes.
