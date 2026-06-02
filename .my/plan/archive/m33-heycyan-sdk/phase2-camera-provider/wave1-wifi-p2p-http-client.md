# M33 Phase 2 — Wave 1: `WiFiP2pHttpClient` (Android)

**Parent:** [`../phase2-camera-provider.md`](../phase2-camera-provider.md)
**Siblings:** [wave2](wave2-heycyan-media-transfer.md) · [wave3](wave3-heycyan-camera-provider.md) · [wave4](wave4-camera-manager-integration.md) · [wave5](wave5-latency-benchmarks.md)

> See [sdk-api-reference.md](../sdk-api-reference.md) for the authoritative
> Android SDK type/method names. BLE notify frames below are
> `GlassesDeviceNotifyRsp` payloads delivered via
> `LargeDataHandler.AddOutDeviceListener(int type, ILargeDataResponse<...>)`
> on the BLE I/O `HandlerThread`.

## Goal

Build the Android-only helper that owns the Wi-Fi P2P group lifecycle, binds
the app process to the P2P network handle, and exposes a process-bound
`HttpClient` rooted at the BLE-reported glasses IP. This is the lowest layer
of the file-based camera pipeline — every byte we read off the glasses
flows through this client. It must be correct on Samsung multi-network
devices and tolerant of the noisy `0x09 0xFF` notify frames the glasses
emit during group formation.

## Steps

1. **Create the project file layout.** Place the new helper at
   [src/BodyCam/Platforms/Android/HeyCyan/WiFiP2pHttpClient.cs](../../../../src/BodyCam/Platforms/Android/HeyCyan/WiFiP2pHttpClient.cs).
   Co-locate a small `BleIpResolver.cs` next to it for parsing the
   `GlassesDeviceNotifyRsp` frame (`LoadData[6] == 0x08`, IPv4 octets in
   `LoadData[7..10]`). Use `LargeDataParser.GetInstance()` for fragment
   reassembly upstream; the resolver only sees fully parsed responses.

2. **Add the Android manifest permissions.** Edit
   [src/BodyCam/Platforms/Android/AndroidManifest.xml](../../../../src/BodyCam/Platforms/Android/AndroidManifest.xml):

   ```xml
   <uses-permission android:name="android.permission.ACCESS_WIFI_STATE" />
   <uses-permission android:name="android.permission.CHANGE_WIFI_STATE" />
   <uses-permission android:name="android.permission.ACCESS_NETWORK_STATE" />
   <uses-permission android:name="android.permission.CHANGE_NETWORK_STATE" />
   <uses-permission android:name="android.permission.ACCESS_FINE_LOCATION" />
   <uses-permission
       android:name="android.permission.NEARBY_WIFI_DEVICES"
       android:usesPermissionFlags="neverForLocation"
       tools:targetApi="33" />
   <uses-permission android:name="android.permission.INTERNET" />
   ```

3. **Allow cleartext to the P2P subnet.** Add
   [src/BodyCam/Platforms/Android/Resources/xml/network_security_config.xml](../../../../src/BodyCam/Platforms/Android/Resources/xml/network_security_config.xml):

   ```xml
   <?xml version="1.0" encoding="utf-8"?>
   <network-security-config>
     <domain-config cleartextTrafficPermitted="true">
       <domain includeSubdomains="true">192.168.49.1</domain>
       <domain includeSubdomains="true">192.168.49.0</domain>
     </domain-config>
   </network-security-config>
   ```

   Wire it into `AndroidManifest.xml` via
   `<application android:networkSecurityConfig="@xml/network_security_config">`.

4. **Implement `WiFiP2pHttpClient`.** Public surface:

   ```csharp
   namespace BodyCam.Services.Glasses.HeyCyan.Android;

   internal sealed class WiFiP2pHttpClient : IAsyncDisposable
   {
       public string? GlassesIp { get; private set; }
       public Uri? BaseUri => GlassesIp is null ? null : new Uri($"http://{GlassesIp}/");

       public Task ConnectAsync(string glassesIp, CancellationToken ct);
       public Task<string> GetStringAsync(string path, CancellationToken ct);
       public Task<byte[]> GetByteArrayAsync(string path, CancellationToken ct);
       public ValueTask DisposeAsync();
   }
   ```

   `ConnectAsync` MUST:
   - Receive the IP from the parsed `GlassesDeviceNotifyRsp` frame where
     `LoadData[6] == 0x08` (octets `[7..10]`) — never read
     `WifiP2pInfo.groupOwnerAddress` (that resolves to `192.168.49.1`,
     which is the **phone**, not the glasses).
   - Wait for `WifiP2pManager.requestConnectionInfo` to report
     `groupFormed == true`.
   - Resolve the P2P `Network` handle from `ConnectivityManager` by
     enumerating `AllNetworks` and matching
     `NetworkCapabilities.HasTransport(TransportType.Wifi)` plus a
     `LinkProperties` route on `192.168.49.0/24`.
   - Call `_connectivity.BindProcessToNetwork(p2pNetwork)` BEFORE
     constructing the `HttpClient`. Without this, requests on Samsung
     route over cellular and time out.
   - Construct `HttpClient` with `Timeout = 20s` and base address
     `http://<glassesIp>/`.

5. **Tolerate the `0x09 0xFF` noise.** During group formation the glasses
   emit `GlassesDeviceNotifyRsp` frames where `LoadData[6] == 0x09` with
   `LoadData[7] == 0xFF`. These are P2P state transitions, not fatal errors.
   The resolver must log-and-ignore and keep waiting for `LoadData[6] == 0x08`
   (or surface a single `TimeoutException` after a configurable deadline,
   default 12s). Note that all `ILargeDataResponse<T>.ParseData` callbacks
   fire on the BLE I/O `HandlerThread` — marshal IP delivery onto a known
   `SynchronizationContext` before signalling `ConnectAsync`'s waiter.

6. **Drain & restore on dispose.** `DisposeAsync` MUST:
   - Dispose the `HttpClient`.
   - Call `_connectivity.BindProcessToNetwork(null)` to restore default
     routing (cellular/regular Wi-Fi).
   - **Not** tear down the P2P group itself — the BLE exit
     `LargeDataHandler.GetInstance().GlassesControl(new byte[] { 0x02, 0x01, 0x09 }, cb)`
     (or reset `0x02 0x01 0x0F`) is the responsibility of
     `IHeyCyanGlassesSession` / Wave 2's `HeyCyanMediaTransfer`.

7. **DI registration.** Register only on Android in
   [src/BodyCam/Platforms/Android/MauiProgram.Android.cs](../../../../src/BodyCam/Platforms/Android/MauiProgram.Android.cs):

   ```csharp
   builder.Services.AddTransient<WiFiP2pHttpClient>();
   builder.Services.AddSingleton<IHeyCyanHttpClientFactory, AndroidHeyCyanHttpClientFactory>();
   ```

   The factory is what Wave 2 consumes; it adapts `WiFiP2pHttpClient`
   behind the cross-platform `IHeyCyanHttpClient` interface.

8. **Smoke test on real hardware.** Use a small dev script /
   instrumentation test:

   ```powershell
   # From the repo root, with a connected Pixel/Samsung and paired glasses:
   pwsh ./deploy-android.ps1
   adb logcat -c
   adb logcat -s DataDownload DeviceNotify HeyCyan.P2p
   ```

   Then in the app trigger a manual "Connect → Enter Transfer Mode →
   GET /files/media.config" path that exercises only Wave 1.

## Verify

- [ ] `WiFiP2pHttpClient.ConnectAsync` returns only after
      `bindProcessToNetwork` succeeds against the resolved P2P network.
- [ ] `GlassesIp` is set from the BLE-reported `GlassesDeviceNotifyRsp`
      where `LoadData[6] == 0x08` (octets `LoadData[7..10]`) (verified by
      unit test against a captured frame).
- [ ] `LoadData[6] == 0x09` with `LoadData[7] == 0xFF` notify frames are
      logged at `Information` and do not throw.
- [ ] `HttpClient` `GET http://<ip>/files/media.config` returns 200 with
      a non-empty body on real hardware.
- [ ] `DisposeAsync` calls `BindProcessToNetwork(null)` and the device
      regains cellular connectivity (verified by curl-ing a public URL
      after dispose).
- [ ] `network_security_config.xml` permits cleartext to
      `192.168.49.0/24`; production builds still reject cleartext to
      arbitrary hosts.
- [ ] `NEARBY_WIFI_DEVICES` declared with `neverForLocation` flag and
      `tools:targetApi="33"`.
- [ ] Unit tests for `BleIpResolver` cover: valid `0x08` frame, short
      buffer, `0x09 0xFF` ignored, multiple frames in one notify burst.
