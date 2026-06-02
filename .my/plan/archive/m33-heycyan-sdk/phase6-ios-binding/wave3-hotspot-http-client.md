# M33 Phase 6 — Wave 3: iOS `HotspotHttpClient`

## Goal

Implement the iOS-side bulk-transfer client used by
`IosHeyCyanGlassesSession.EnterTransferModeAsync`. It wraps
`NEHotspotConfigurationManager` to join the glasses' Wi-Fi hotspot, probes a
fixed list of candidate IPs to discover the glasses' HTTP server, and then
defers all parsing to the shared `MediaConfigParser` already used by
Android. Behaviour must mirror the iOS demo's
`GlassesWiFiHandler.m` / `WiFiTransferManager.m` and the
[`WIFI_TRANSFER_ARCHITECTURE.md`](../../../../Alternative-HeyCyan-App-and-SDK/WIFI_TRANSFER_ARCHITECTURE.md).

**Parent phase:** [`../phase6-ios-binding.md`](../phase6-ios-binding.md)
**Prev:** [`wave2-ios-glasses-session.md`](wave2-ios-glasses-session.md)
**Next:** [`wave4-infoplist-entitlements.md`](wave4-infoplist-entitlements.md)

## Steps

1. **Create the file.** `src/BodyCam/Platforms/iOS/HeyCyan/HotspotHttpClient.cs`,
   internal sealed, owns a single long-lived `HttpClient`. Constants:

   ```csharp
   private static readonly string[] CandidateIps =
   {
       "192.168.43.1", // Android-style tethering subnet (most common)
       "192.168.4.1",  // ESP32 / SoftAP default
       "192.168.1.1",  // Home-router style
       "192.168.0.1",  // Home-router alt
       "10.0.0.1",     // Carrier-style fallback
   };

   private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(800);
   private const string FallbackPassword = "123456789";
   private const string MediaConfigPath = "/files/media.config";
   ```

   The order is taken from `discoverGlassesIP` in the demo — keep it
   identical so Android and iOS test fixtures stay aligned.

2. **Implement `JoinAsync`.** Build an `NEHotspotConfiguration` with
   `JoinOnce = YES` so the OS tears the SSID profile back down when the
   process exits (no Wi-Fi profile leaks into the user's saved networks).
   Use the password reported by `QCSDKCmdCreator.OpenWifi` if non-null,
   otherwise the QCSDK convention `"123456789"`:

   ```csharp
   using NetworkExtension;

   public Task JoinAsync(string ssid, string? password, CancellationToken ct)
   {
       var config = new NEHotspotConfiguration(
           ssid,
           password ?? FallbackPassword,
           isWep: false)
       {
           JoinOnce = true,
       };

       var tcs = new TaskCompletionSource<bool>();
       NEHotspotConfigurationManager.SharedManager.ApplyConfiguration(config, err =>
       {
           if (err is null) tcs.TrySetResult(true);
           else tcs.TrySetException(new IOException(
               $"NEHotspotConfiguration failed: {err.LocalizedDescription} (code {err.Code})"));
       });
       using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
       return tcs.Task;
   }
   ```

3. **Implement `DiscoverGlassesIpAsync`.** Try each candidate IP with a
   short per-probe timeout (≤ 1s) so a missing subnet doesn't dominate the
   end-to-end transfer latency. The first IP that returns `200 OK` for
   `GET /files/media.config` wins:

   ```csharp
   public async Task<string> DiscoverGlassesIpAsync(CancellationToken ct)
   {
       foreach (var ip in CandidateIps)
       {
           ct.ThrowIfCancellationRequested();
           using var probe = new CancellationTokenSource(ProbeTimeout);
           using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, probe.Token);
           try
           {
               using var resp = await _http.GetAsync($"http://{ip}{MediaConfigPath}", linked.Token)
                   .ConfigureAwait(false);
               if (resp.IsSuccessStatusCode) return $"http://{ip}";
           }
           catch (Exception) when (!ct.IsCancellationRequested)
           {
               // Probe failed (timeout, refused, no route) — try next candidate.
           }
       }
       throw new IOException(
           $"No HeyCyan glasses found at any of: {string.Join(", ", CandidateIps)}");
   }
   ```

4. **Implement `GetMediaConfigAsync`.** Delegate parsing to the shared
   `MediaConfigParser` from M33 Phase 2. iOS must not have its own parser
   — the format (`name|size|sha`-style line records) is identical between
   platforms and a duplicate parser would drift over time:

   ```csharp
   public async Task<IReadOnlyList<string>> GetMediaConfigAsync(string baseUrl, CancellationToken ct)
   {
       using var resp = await _http.GetAsync($"{baseUrl}{MediaConfigPath}", ct).ConfigureAwait(false);
       resp.EnsureSuccessStatusCode();
       var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
       return MediaConfigParser.ParseFileNames(text);
   }
   ```

5. **Stream individual files.** Bulk transfer reuses `HttpClient` but must
   stream to disk, not buffer in memory — videos can exceed 100 MB:

   ```csharp
   public async Task DownloadFileAsync(string baseUrl, string name, Stream dest, CancellationToken ct)
   {
       using var resp = await _http.GetAsync(
               $"{baseUrl}/files/{Uri.EscapeDataString(name)}",
               HttpCompletionOption.ResponseHeadersRead,
               ct)
           .ConfigureAwait(false);
       resp.EnsureSuccessStatusCode();
       await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
       await src.CopyToAsync(dest, 64 * 1024, ct).ConfigureAwait(false);
   }
   ```

6. **Tear down on dispose.** `HotspotHttpClient` implements
   `IAsyncDisposable` and removes its configuration explicitly so Wi-Fi
   returns to the user's previous network the moment transfer mode ends:

   ```csharp
   public ValueTask DisposeAsync()
   {
       NEHotspotConfigurationManager.SharedManager.RemoveConfiguration(_currentSsid);
       _http.Dispose();
       return default;
   }
   ```

7. **Cancellation discipline.** Every public method honours the caller's
   `CancellationToken`. `JoinAsync` uses `ct.Register` because
   `ApplyConfiguration` has no native cancellation; `DiscoverGlassesIpAsync`
   and `GetMediaConfigAsync` pass it directly into `HttpClient`.

8. **Wire into DI.** Registered as a singleton in
   [`wave5-di-and-parity-tests.md`](wave5-di-and-parity-tests.md) so a
   single `NEHotspotConfiguration` lifecycle covers consecutive captures
   that keep the hotspot warm (Phase 2 amortization).

## Verify

- [ ] `JoinAsync` sets `JoinOnce = true` (no profile leaks into user's
      saved networks)
- [ ] Falls back to password `"123456789"` when `OpenWifi` returns `nil`
- [ ] `DiscoverGlassesIpAsync` probes the exact candidate list
      `192.168.43.1, 192.168.4.1, 192.168.1.1, 192.168.0.1, 10.0.0.1` in
      that order
- [ ] Per-probe timeout ≤ 1s; total bounded by caller's `CancellationToken`
- [ ] `GetMediaConfigAsync` delegates to the shared `MediaConfigParser`
      (no iOS-local parser)
- [ ] `DownloadFileAsync` streams via `HttpCompletionOption.ResponseHeadersRead`
      (no full-buffer of large MP4s)
- [ ] `DisposeAsync` calls
      `NEHotspotConfigurationManager.RemoveConfiguration` so Wi-Fi reverts
- [ ] Surfaces `IOException` (not `NSError`) on every failure path so
      cross-platform callers can catch one type
