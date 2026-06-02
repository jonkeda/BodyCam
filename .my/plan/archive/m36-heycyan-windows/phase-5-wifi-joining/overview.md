# Phase 5 — Windows WiFi Hotspot Joining

**Status:** Research complete — blocked on WiFi Direct capability
**Depends on:** Phase 2 (BLE session — **complete**), Phase 3 (HTTP client factory — **complete**)
**Research document:** [wifi-hotspot-joining-research.md](../../../docs/functionality/wifi-hotspot-joining-research.md)
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

## Investigation Results (2026-05-17)

### Question 1: Does the glasses' WiFi appear as a regular AP?

**NO.** After sending `{0x02, 0x01, 0x04}`, regular WiFi scans show only
household networks. The glasses use **WiFi Direct (P2P)**, not a standard
AP. WiFi Direct peers are invisible to `WiFiAdapter.ScanAsync()`.

### Question 2: What is the SSID?

**Not applicable for WiFi Direct.** The iOS SDK returns SSID/password via
the `openWifiWithMode:` BLE callback (proprietary QCSDK framework). We
don't receive an SSID frame — only a late type-0x01 notification of unknown
meaning.

### Question 3: What is the password?

**Not applicable for WiFi Direct.** iOS uses `"123456789"` as fallback
password for standard hotspot mode. WiFi Direct uses WPS PBC (push-button
config) with no credentials.

### Question 4: Does WiFi Direct work on Windows?

**Not yet.** `DeviceWatcher` with `WiFiDirectDevice.GetDeviceSelector
(AssociationEndpoint)` finds zero peers. Most likely cause: the unpackaged
app (dotnet test / WindowsPackageType=None) lacks the `wifiDirect` device
capability at runtime. Needs testing from a packaged MSIX deployment.

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

- [x] Glasses WiFi network type determined (**WiFi Direct**, not standard AP)
- [x] SSID and password discovery method documented (N/A for WiFi Direct; iOS-only via QCSDK)
- [ ] Windows programmatically joins glasses WiFi after BLE transfer command — **BLOCKED**
- [ ] HTTP requests to glasses IP succeed (`GET /files/media.config`) — **BLOCKED**
- [ ] WiFi cleanup restores previous network on exit
- [x] `Package.appxmanifest` updated with required capabilities (`wifiDirect`, `bluetooth`, `wifiControl`)

## Critical Finding: BLE Frame Misidentification

The 14-byte frame received after ~60–90 seconds:

```
BC-73-08-00-FA-C7-01-0B-00-00-00-00-00-01
```

Was initially identified as a "0x08 IP notify" but is actually **type 0x01**
(`frame[6] = 0x01`). The `0x08` at `frame[2]` is the payload length, not the
notify type. Our `TryParseTransferIp` (which checks `frame[6] == 0x08`)
correctly rejects this frame. See the research document for full analysis.

## Next Steps

See [research document](../../../docs/functionality/wifi-hotspot-joining-research.md)
Section 8 for prioritized next steps. Key actions:

1. **P0:** Check WiFi Direct driver support (`netsh wlan show drivers`)
2. **P0:** Parse the type-0x01 notification (may contain mode status)
3. **P1:** Start WiFi Direct discovery BEFORE sending BLE command (match Android)
4. **P1:** Send ResetP2P (`{0x02, 0x01, 0x0F}`) before EnterTransferMode
5. **P2:** Deploy as packaged MSIX and test WiFi Direct with proper capabilities
