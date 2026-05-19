# RCA-813: BLE-to-WiFi Transfer Flow (Working)

**Date:** 2025-05-18  
**Status:** RESOLVED  
**Depends on:** RCA-810, RCA-812

## Summary

Successfully established WiFi connection to HeyCyan glasses for media transfer on Windows.
The glasses use a **hidden WiFi network** (not visible in standard scans) that can be
joined using a WLAN profile with credentials received via BLE.

## Working Flow

### Step 1: BLE Command — Enter Transfer Mode

Send `[0x02, 0x01, 0x04]` wrapped in action 0x41 Serial Port frame:
```
TX: BC-41-03-00-{crc}-{crc}-02-01-04
```

Key discovery: The Android SDK sends 3 bytes `[0x02, 0x01, 0x04]`, not 2 bytes `[0x01, 0x04]`.
The `0x02` prefix is required to trigger the glasses' WiFi AP.

### Step 2: BLE Notification — Receive SSID + Password

The glasses respond with a 0x41 notification containing credentials:
```
RX: BC-41-25-00-{crc}-{crc}-02-01-04-01-14-00-09-00-4D-30-31-20-50-72-6F-5F-...
```

**Payload format:**
| Offset | Length | Value | Meaning |
|--------|--------|-------|---------|
| 0-2    | 3      | 02-01-04 | Command echo |
| 3      | 1      | 01       | Status (success) |
| 4-5    | 2 (LE) | 14-00 = 20 | SSID length |
| 6-7    | 2 (LE) | 09-00 = 9  | Password length |
| 8+     | 20     | ASCII    | SSID: "M01 Pro_D879B87FE6C9" |
| 28+    | 9      | ASCII    | Password: "123456789" |

**SSID pattern:** `{BLE_Name}_{MAC_no_colons}` → "M01 Pro_D879B87FE6C9"

### Step 3: Connect to Hidden WiFi via WLAN Profile

The glasses' WiFi is NOT visible in regular scans (it's a hidden/non-broadcast network).
Connection requires creating a manual WLAN profile:

```xml
<WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
    <name>M01 Pro_D879B87FE6C9</name>
    <SSIDConfig>
        <SSID><name>M01 Pro_D879B87FE6C9</name></SSID>
        <nonBroadcast>true</nonBroadcast>
    </SSIDConfig>
    <connectionType>ESS</connectionType>
    <connectionMode>manual</connectionMode>
    <MSM>
        <security>
            <authEncryption>
                <authentication>WPA2PSK</authentication>
                <encryption>AES</encryption>
            </authEncryption>
            <sharedKey>
                <keyType>passPhrase</keyType>
                <protected>false</protected>
                <keyMaterial>123456789</keyMaterial>
            </sharedKey>
        </security>
    </MSM>
</WLANProfile>
```

Commands:
```
netsh wlan add profile filename="<temp>.xml"  → "Profile M01 Pro_D879B87FE6C9 is added on interface Wi-Fi."
netsh wlan connect name="M01 Pro_D879B87FE6C9" → "Connection request was completed successfully."
```

### Step 4: Get Gateway IP (= Glasses HTTP Server)

After DHCP lease, the gateway IP is the glasses' HTTP server: **192.168.1.1**

### Step 5: Fetch Media List

```
GET http://192.168.1.1/files/media.config
```

Returns newline-separated file names.

### Step 6: Download Files

```
GET http://192.168.1.1/files/{filename}
```

## What Failed (and Why)

| Strategy | Result | Why |
|----------|--------|-----|
| WiFi Direct peer discovery | Intermittent (found once, then never again) | Glasses stop P2P advertising after first failed connection |
| WiFi Direct FromIdAsync | Empty exception | No pairing/PIN exchange implemented (partially fixed but not needed) |
| Regular WiFi scan for hotspot | Never found | Hidden network — not broadcast |
| Force WLAN profile (netsh) | **SUCCESS** | Works for hidden networks |
| BLE 0x08 IP notification | Never received | Glasses only send this when P2P succeeds on their end |

## Timing

- BLE command → credential notification: ~1 second
- WiFi connect via profile: ~2 seconds
- DHCP gateway available: ~5-15 seconds (first attempt failed at 10s, second succeeded)
- Total: ~15-20 seconds (could be faster with longer initial gateway wait)

## Files Modified

- `HeyCyanCommands.cs` — 3-byte payload `[0x02, 0x01, 0x04]`
- `WindowsGlassesWiFiManager.cs` — `ForceJoinAsync()` via netsh WLAN profile
- `WindowsHeyCyanGlassesSession.cs` — Parse BLE SSID+password notification, call ForceJoin
- `WindowsWiFiDirectManager.cs` — Pairing + retry logic (secondary strategy)

## Next Steps

- [ ] Take a photo via BLE command and download it (RCA-813 continuation)
- [ ] Increase initial gateway wait from 10s to 15s (first attempt sometimes fails)
- [ ] Clean up: remove/simplify WiFi Direct code (force-join is the primary path)
- [ ] Exit transfer mode when done (`HeyCyanCommands.ExitTransferMode()`)
