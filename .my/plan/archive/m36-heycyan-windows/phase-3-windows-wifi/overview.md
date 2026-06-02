# Phase 3 — Windows WiFi Transfer

**Status:** Proposed
**Depends on:** Phase 2 (BLE commands work, can enter transfer mode)
**Sibling phases:** [Phase 1 — BLE Discovery](../phase-1-ble-discovery/overview.md), [Phase 2 — Windows BLE](../phase-2-windows-ble/overview.md), [Phase 4 — Integration](../phase-4-integration/overview.md)

---

## Summary

After BLE sends the "enter transfer mode" command, the glasses activate a
WiFi hotspot and report their IP via a BLE notify frame. On Windows, we need
to join that WiFi network and download media over HTTP. The HTTP download
protocol is already implemented in the shared `HeyCyanMediaTransfer` — this
phase only adds the Windows-specific WiFi joining layer.

---

## 3.1 — WiFi network joining

### Transfer mode activation flow

1. `EnterTransferModeAsync()` sends BLE command `0x02 0x01 0x04`
2. Glasses start WiFi Direct hotspot
3. BLE notify frame arrives with `loadData[6] == 0x08`, IP in bytes 7-10
4. **Windows joins the glasses' WiFi network** ← this phase
5. Standard `HttpClient` downloads from `http://{ip}/files/...`

### Windows WiFi APIs

**Option A — Windows.Devices.WiFi (WinRT, preferred)**

```csharp
using Windows.Devices.WiFi;

var adapter = (await WiFiAdapter.FindAllAdaptersAsync()).First();
await adapter.ScanAsync();
var network = adapter.NetworkReport.AvailableNetworks
    .FirstOrDefault(n => n.Ssid == glassesSsid);
var result = await adapter.ConnectAsync(network, WiFiReconnectionKind.Manual,
    new PasswordCredential("", "", password));
```

> Requires `wifiControl` device capability in the app manifest.

**Option B — Native WLAN API (P/Invoke fallback)**

If the WinRT API is insufficient (e.g., cannot connect to ad-hoc networks):

```csharp
[DllImport("wlanapi.dll")]
static extern uint WlanConnect(IntPtr hClient, ref Guid interfaceGuid,
    ref WLAN_CONNECTION_PARAMETERS pConnectionParameters, IntPtr pReserved);
```

### SSID and password discovery

The glasses' SSID and password may be:
- Broadcast via BLE notify (check if any frame type carries SSID/password)
- Hardcoded defaults (iOS implementation uses fallback `"123456789"`)
- Derived from device name/MAC

> Check Phase 1 BLE sniffing for any frames carrying WiFi credentials.

---

## 3.2 — HTTP client factory

Create `src/BodyCam/Platforms/Windows/HeyCyan/WindowsHeyCyanHttpClientFactory.cs`:

```csharp
internal sealed class WindowsHeyCyanHttpClientFactory : IHeyCyanHttpClientFactory
{
    public HttpClient Create(string baseUrl)
    {
        // On Windows, no special network binding is needed (unlike Android
        // which must bind to the P2P network). Standard HttpClient routes
        // to the glasses IP automatically once WiFi is joined.
        return new HttpClient { BaseAddress = new Uri(baseUrl) };
    }
}
```

Unlike Android (which needs `BindProcessToNetwork` to avoid routing over
cellular), Windows typically has a single active network — or if both WiFi
and Ethernet are active, the glasses' subnet route will be preferred
automatically.

---

## 3.3 — IP resolution and transfer session

The IP resolution logic already exists in `HeyCyanFrameParser` (notify type
`0x08`, bytes 7-10). The `WindowsHeyCyanGlassesSession.EnterTransferModeAsync`
(from Phase 2) should:

1. Send `HeyCyanCommands.EnterTransferMode` via BLE
2. Wait for notify frame `0x08` → extract IP
3. Join WiFi network (3.1)
4. Construct `HeyCyanTransferSession` with `BaseUrl = $"http://{ip}"`
5. Verify connectivity: `GET /files/media.config`

The shared `HeyCyanMediaTransfer` then handles the actual file downloads
using the `IHeyCyanHttpClientFactory`.

---

## 3.4 — Cleanup and disconnect

On `ExitTransferModeAsync`:

1. Send BLE command `0x02 0x01 0x09` (exit transfer mode)
2. Disconnect from glasses WiFi network
3. Windows will auto-reconnect to the previous WiFi network

---

## Notes

- The glasses' WiFi hotspot is temporary — it's only active during transfer
  mode. Joining it will temporarily disconnect from the user's regular WiFi.
  Consider showing a UI warning.
- Windows 10 may require manual WiFi profile management. Windows 11 has
  simpler APIs.
- If the glasses use WiFi Direct (not a standard hotspot), Windows WiFi
  Direct APIs (`Windows.Devices.WiFiDirect`) may be needed instead.

---

## Acceptance

- [ ] Windows can join the glasses' WiFi hotspot programmatically.
- [ ] `WindowsHeyCyanHttpClientFactory` creates working `HttpClient`.
- [ ] `GET /files/media.config` returns valid JSON from glasses.
- [ ] File download works end-to-end (trigger transfer mode → download photo).
- [ ] WiFi cleanup restores previous network on exit.
