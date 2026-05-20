# HeyCyan Glasses WiFi Connection Next Steps

**Date:** 2026-05-19
**Goal:** Get Windows connected to the glasses WiFi transport well enough to fetch `/files/media.config` and download photos.

## Short Answer

The Wi-Fi Framework spike has now been tried. It improved diagnostics and proved
the commercial framework can discover the glasses WiFi Direct peer, but it still
does not establish a routed Windows connection to the glasses.

We have tried the native Windows paths:

- BLE control path for photo and transfer mode.
- Native WinRT WiFi Direct discovery and pairing.
- Native Windows WLAN/hotspot join using generated profiles and `netsh`.

The remaining high-value experiment is now a second WiFi adapter or an Android
bridge. The current Intel BE200/Windows stack can see the peer but cannot finish
the P2P connection.

## Result Update: Wi-Fi Framework Spike

Implemented a WCL-backed transport mode in `src/BodyCam.HeyCyan.Probe`:

```powershell
src\BodyCam.HeyCyan.Probe\bin\Debug\net10.0-windows10.0.19041.0\BodyCam.HeyCyan.Probe.exe --mac D8:79:B8:7F:E6:C9 --transport wcl-wifi-direct --download-existing --timeout-seconds 180 --verbose
```

Observed:

- BLE connects after adding retry/cached-service fallback for flaky GATT service discovery.
- Transfer mode starts.
- Glasses return:
  - SSID/device name: `M01 Pro_D879B87FE6C9`
  - password length: 9 chars
  - transfer IP: `192.168.31.1`
- WCL WiFi Direct watcher finds the peer:
  - `WiFiDirect#60:C2:2A:1A:B6:1B`
  - `M01 Pro_D879B87FE6C9`
- WCL stale-pairing check reports `paired=False`, so stale pairing is not the immediate blocker.
- WCL client connect with `Invitation/PushButton` requests pair params, then fails:
  - pairing result `0x00028013` = `WCL_E_WIFI_DIRECT_PAIR_FAILURE`
  - device connection error `0x0002900B` = `WCL_E_WIFI_DIRECT_DEVICE_CONNECTION_TERMINATED_BT_USER`
- WCL client connect with `GroupOwnerNegotiation/PushButton` fails:
  - `0x00013009` = `WCL_E_WINRT_ASYNC_OPERATION_ERROR`
- Native hotspot fallback still only reaches `associating`, then `disconnected`.
- No HTTP route to `192.168.31.1` is created; no photo download yet.

Log files from the most useful WCL runs:

- `.my/logs/heycyan/wcl-20260519-160621.out.log`
- `.my/logs/heycyan/wcl-20260519-160621.err.log`
- `.my/logs/heycyan/wcl-20260519-160910.out.log`
- `.my/logs/heycyan/wcl-20260519-160910.err.log`

Interpretation:

This strongly suggests the blocker is not just our WinRT implementation. WCL uses
its own wrapper over Windows WiFi APIs and reaches the same boundary: peer
discovery works, pairing/route establishment fails.

## Current Findings

The test probe now proves several important pieces work:

- BLE connects to the glasses.
- Photo trigger commands are accepted.
- Transfer mode command `02 01 04` is accepted.
- The glasses return transfer credentials:
  - SSID/device name: `M01 Pro_D879B87FE6C9`
  - password length: 9 chars, observed default-compatible value
- `GetWifiIP` returns `192.168.31.1`.
- Windows can now discover the WiFi Direct peer:
  - `WiFiDirect#60:C2:2A:1A:B6:1B`
  - name: `M01 Pro_D879B87FE6C9`

The remaining blocker is route establishment. Windows never creates a usable WiFi network path to `192.168.31.1`.

## What Failed

### Native WiFi Direct

Test command:

```powershell
$env:BODYCAM_REAL_HEYCYAN_MAC="D8:79:B8:7F:E6:C9"
dotnet run --project src/BodyCam.HeyCyan.Probe -- --download-existing --transport wifi-direct --verbose
```

Observed:

- `DeviceWatcher` finds the glasses peer.
- Pairing reaches WPS PushButton / `ConfirmOnly`.
- Windows accepts the pairing request.
- `PairAsync` returns `Failed`, or `FromIdAsync` later throws a COM exception.
- No endpoint pair is created.
- No HTTP route exists.

Variants already tried:

- Start WiFi Direct discovery before BLE transfer mode.
- Send `GetDeviceConfig` (`0x47`) before connecting.
- Poll `GetWifiIP` during setup.
- WPS PushButton configuration.
- PIN fallback with the BLE password.
- `GroupOwnerIntent = 0`.
- `GroupOwnerIntent = 15`.
- `PreferredPairingProcedure = GroupOwnerNegotiation`.
- `PreferredPairingProcedure = Invitation` (worse: pairing did not reach the same useful `ConfirmOnly` point).
- `DevicePairingProtectionLevel.Default`.
- `DevicePairingProtectionLevel.None`.

Best native result so far: peer discovery works, but pairing fails.

### Native Hotspot/WLAN

Test command:

```powershell
dotnet run --project src/BodyCam.HeyCyan.Probe -- --download-existing --transport hotspot --verbose
```

Observed:

- Windows accepts the generated WLAN profile.
- `netsh wlan connect` reports the connect request was accepted.
- WLAN state briefly becomes `associating`.
- It then returns to `disconnected`.
- WLAN AutoConfig logs:
  - failure reason: `The specific network is not available.`
  - `RSSI: 255`

Interpretation:

The name returned by BLE is visible as a WiFi Direct peer, but Windows does not see it as a normal infrastructure AP. Treating it as an iOS-style regular hotspot is probably the wrong path on this hardware/firmware, unless there is a different BLE command that forces legacy AP mode.

## Wi-Fi Framework Evaluation

Source: https://www.btframework.com/wififramework.htm#download

The Wi-Fi Framework page says the .NET Edition supports modern .NET on Windows and can be used by console apps and .NET MAUI apps. Its listed WiFi Direct features include:

- WiFi Direct devices watcher.
- WiFi Direct client.
- Enumerating paired WiFi Direct devices.
- Mobile Hotspot control.
- Network adapter and IP inspection.

The download table currently lists version `7.12.11.0` for the .NET Edition demo.

Important licensing/demo note:

- The demo shows an "Unregistered version" dialog.
- The demo is for trial/evaluation only.
- Do not commit demo binaries into the repo.
- If it works, decide whether to buy a license before integrating it into product code.

## Proposed Plan

### Phase 1: Preserve the Native Probe as Baseline

Keep `src/BodyCam.HeyCyan.Probe` as the repeatable real-device test program.

Use three baseline commands:

```powershell
dotnet run --project src/BodyCam.HeyCyan.Probe -- --download-existing --transport wifi-direct --verbose
dotnet run --project src/BodyCam.HeyCyan.Probe -- --download-existing --transport hotspot --verbose
dotnet run --project src/BodyCam.HeyCyan.Probe -- --download-existing --transport both --verbose
```

Before each run:

- Power-cycle or exit transfer mode on the glasses.
- Force-stop official Android/iOS HeyCyan apps so they do not race the P2P session.
- Keep the glasses close to the PC.
- Record Windows WiFi driver version and adapter name.

Success criteria:

- Windows reports a WiFi Direct endpoint pair or a WLAN association.
- `http://192.168.31.1/files/media.config` returns non-HTML media text.
- Probe downloads a JPEG and validates SOI/EOI bytes.

### Phase 2: Build a Wi-Fi Framework Spike

Status: completed as a probe-only spike. Result: discovery works; connection fails.

Create a throwaway probe outside production code, for example:

```text
.my/tools/wififramework-probe/
```

Do not commit the Wi-Fi Framework DLLs or installer output.

Spike responsibilities:

1. Load the Wi-Fi Framework .NET Edition demo DLLs locally.
2. Enumerate WiFi adapters and log capabilities.
3. Start WiFi Direct device watcher before BLE transfer mode.
4. Trigger transfer mode with the existing BLE session code.
5. Verify whether the framework sees:
   - `M01 Pro_D879B87FE6C9`
   - `WiFiDirect#60:C2:2A:1A:B6:1B`
6. Attempt WiFi Direct client connection using WPS PushButton/PBC.
7. Log local IP, remote IP, paired device information, and connection state.
8. Probe:

```text
http://192.168.31.1/files/media.config
```

Expected outcomes:

| Outcome | Meaning | Next Action |
|---|---|---|
| Framework cannot discover peer | Likely adapter/driver/Windows limitation | Try second WiFi adapter or Android bridge |
| Framework discovers peer but cannot pair | Confirms native failure is not just our code | Try second adapter, driver update, or bridge |
| Framework pairs but no HTTP route | Need route/IP binding or endpoint selection work | Inspect local/remote IPs and add route/probe logic |
| Framework pairs and HTTP works | Buy/license candidate; wrap behind `IWindowsP2pTransport` |

### Phase 3: Try a Second WiFi Adapter

Current adapter observed:

```text
Intel(R) Wi-Fi 7 BE200 320MHz
```

The BE200 can see the peer, but pairing fails. That may be:

- Windows API limitation.
- Driver behavior.
- Glasses firmware compatibility.
- Adapter-specific WiFi Direct behavior.

Try a second known-good USB WiFi adapter with WiFi Direct/Miracast support. Run the native probe and Wi-Fi Framework spike on both adapters.

Record:

- Adapter model.
- Driver provider and version.
- Whether peer discovery works.
- Whether WPS/PBC pairing works.
- Whether endpoint pairs appear.
- Whether HTTP works.

### Phase 4: Native WinRT Follow-Ups

These are lower priority than the Wi-Fi Framework spike, but still useful:

1. Test `WiFiDirectDevice.FromIdAsync` without an explicit pre-`PairAsync`.
   - Current code pairs first, then calls `FromIdAsync`.
   - A no-prepair path might let Windows do the full negotiation internally.

2. Split pairing variants into separate one-shot runs.
   - PushButton only.
   - ConfirmOnly only.
   - ProvidePin only.
   - No custom pairing handler.

3. Add richer pairing diagnostics.
   - Log `DevicePairingResult.ProtectionLevelUsed`.
   - Log exact COM HRESULT names.
   - Log paired/unpaired state before and after.

4. Ensure stale WiFi Direct devices are removed.
   - Unpair programmatically.
   - Also check Windows Settings > Bluetooth & devices for stale `M01 Pro...` / WiFi Direct entries.

### Phase 5: Revisit Legacy Hotspot Only If We Find a Different BLE Mode

The native hotspot attempt currently looks like a dead end because Windows does not see the returned name as an infrastructure AP.

Only continue this path if research finds a separate vendor command for legacy AP/hotspot mode, distinct from Android P2P transfer mode.

Search targets:

- iOS SDK `openWifiWithMode`.
- Android SDK `glassesControl` variants.
- Decompilation references to AP, hotspot, SoftAP, WiFi mode, transfer mode, and `QCOperatorDeviceModeTransfer`.

Success criteria for hotspot mode:

- `netsh wlan show networks` lists the glasses as an infrastructure SSID.
- WLAN AutoConfig reports a real RSSI, not `255`.
- Windows gets a DHCP IP.
- Gateway or BLE IP serves `/files/media.config`.

### Phase 6: Android Bridge as Fallback

If native Windows and Wi-Fi Framework both fail, the pragmatic route is an Android helper/bridge:

1. Android connects to glasses via the known-working P2P flow.
2. Android downloads media from glasses.
3. Android exposes the files to Windows via one of:
   - ADB pull.
   - Local LAN HTTP relay.
   - USB tether/local socket relay.
   - A small companion app endpoint.

This is not ideal for product UX, but it can unblock validation of photo capture/download while Windows P2P remains unresolved.

## Recommended Next Action

Stop spending time on native Windows P2P on this adapter until a second WiFi
adapter has been tested.

Best next options:

1. Test a second USB WiFi adapter with WiFi Direct/Miracast support.
2. Test the same glasses on an unmanaged laptop to rule out endpoint-security policy.
3. Build the Android helper/bridge fallback so photo capture/download can be validated while native Windows P2P remains blocked.
