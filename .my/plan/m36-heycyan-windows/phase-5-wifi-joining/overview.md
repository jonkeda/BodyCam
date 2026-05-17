# Phase 5 — Windows WiFi Hotspot Joining

**Status:** Proposed
**Depends on:** Phase 2 (BLE session — **complete**), Phase 3 (HTTP client factory — **complete**)
**Sibling phases:** [Phase 1](../phase-1-ble-discovery/overview.md), [Phase 2](../phase-2-windows-ble/overview.md), [Phase 3](../phase-3-windows-wifi/overview.md), [Phase 4](../phase-4-integration/overview.md)

---

## Problem

Phase 2 sends the BLE "enter transfer mode" command and receives the glasses'
IP address. Phase 3 downloads files over HTTP once the network is reachable.
But nothing currently **joins** the glasses' WiFi network on Windows — the
HTTP requests will fail because the glasses' IP is unreachable.

### How other platforms do it

| Platform | Mechanism | SSID/Password | Network binding |
|---|---|---|---|
| Android | WiFi Direct P2P (`WifiP2pManager`) | Auto-negotiated, no credentials | `ConnectivityManager.BindProcessToNetwork()` |
| iOS | Standard hotspot (`NEHotspotConfiguration`) | SSID from BLE `openWifiWithMode`, password = `"123456789"` fallback | None needed |

The glasses support **both** mechanisms — WiFi Direct for Android, standard
hotspot for iOS. Windows must use one or the other.

---

## Investigation needed

Before implementation, determine which WiFi mode the glasses offer Windows:

### Question 1: Does the glasses' WiFi appear as a regular AP?

When the glasses enter transfer mode, does a standard WiFi network (SSID)
appear in the Windows WiFi network list? If yes, this is the simpler path
(Option A below).

**Test:** Enter transfer mode via BLE from the BodyCam app (Phase 2 is
implemented), then check available WiFi networks in Windows Settings or
`netsh wlan show networks`.

### Question 2: What is the SSID?

The SSID is sent by the glasses via BLE. Check:
- Does `WindowsHeyCyanGlassesSession` receive a notify frame carrying
  the SSID during transfer mode entry? (May be a frame type not yet
  parsed by `HeyCyanFrameParser`.)
- Or is the SSID derived from the device name / MAC?

### Question 3: What is the password?

- iOS uses `"123456789"` as fallback. Same on Windows?
- Or does the BLE frame carry the password alongside the SSID?

---

## Option A — Standard WiFi hotspot (`WiFiAdapter`, preferred)

If the glasses appear as a regular WiFi AP:

```csharp
using Windows.Devices.WiFi;
using Windows.Security.Credentials;

internal sealed class WindowsGlassesWifiManager
{
    private WiFiAdapter? _adapter;
    private string? _previousSsid;

    public async Task JoinAsync(string ssid, string password, CancellationToken ct)
    {
        var adapters = await WiFiAdapter.FindAllAdaptersAsync().AsTask(ct);
        _adapter = adapters.FirstOrDefault()
            ?? throw new InvalidOperationException("No WiFi adapter found");

        // Remember current network for restoration
        _previousSsid = GetCurrentSsid();

        await _adapter.ScanAsync().AsTask(ct);
        var network = _adapter.NetworkReport.AvailableNetworks
            .FirstOrDefault(n => n.Ssid == ssid)
            ?? throw new InvalidOperationException($"Glasses WiFi '{ssid}' not found");

        var credential = new PasswordCredential { Password = password };
        var result = await _adapter.ConnectAsync(
            network,
            WiFiReconnectionKind.Manual,
            credential).AsTask(ct);

        if (result.ConnectionStatus != WiFiConnectionStatus.Success)
            throw new InvalidOperationException(
                $"Failed to join glasses WiFi: {result.ConnectionStatus}");
    }

    public async Task LeaveAsync(CancellationToken ct)
    {
        _adapter?.Disconnect();
        // Windows auto-reconnects to the previous known network
    }

    private static string? GetCurrentSsid()
    {
        // Use NetworkInformation.GetConnectionProfiles() to find the
        // current WiFi SSID for later restoration
        return null; // TODO
    }
}
```

**Requires:** `wifiControl` device capability in `Package.appxmanifest`:
```xml
<DeviceCapability Name="wifiControl" />
```

**Note:** `WiFiAdapter` requires the app to run with the `wifiControl`
capability AND may require the Windows Location Service to be enabled
(WiFi scanning depends on it on some Windows builds).

---

## Option B — WiFi Direct (`WiFiDirectDevice`)

If the glasses use WiFi Direct (like Android P2P):

```csharp
using Windows.Devices.WiFiDirect;

var deviceSelector = WiFiDirectDevice.GetDeviceSelector();
// Discover and connect to the glasses' WiFi Direct group
var device = await WiFiDirectDevice.FromIdAsync(deviceId).AsTask(ct);
var endpoints = device.GetConnectionEndpointPairs();
// Use the endpoint IP for HTTP transfers
```

WiFi Direct on Windows is more complex and may require device pairing.
This is the fallback if Option A doesn't work.

---

## Integration with `WindowsHeyCyanGlassesSession`

Once the WiFi joining mechanism is determined, update
`EnterTransferModeAsync` to:

1. Send BLE enter-transfer command (already done)
2. Wait for glasses IP via BLE notify 0x08 (already done)
3. **Wait for SSID/password from BLE** (if separate frame) or **derive
   from known pattern**
4. **Join glasses WiFi** via `WindowsGlassesWifiManager.JoinAsync()`
5. Return `HeyCyanTransferSession` (already done)

And `ExitTransferModeAsync` to:

1. Send BLE exit-transfer command (already done)
2. **Leave glasses WiFi** via `WindowsGlassesWifiManager.LeaveAsync()`

---

## Acceptance

- [ ] Glasses WiFi network type determined (standard AP vs WiFi Direct)
- [ ] SSID and password discovery method documented
- [ ] Windows programmatically joins glasses WiFi after BLE transfer command
- [ ] HTTP requests to glasses IP succeed (`GET /files/media.config`)
- [ ] WiFi cleanup restores previous network on exit
- [ ] `Package.appxmanifest` updated with required capabilities
