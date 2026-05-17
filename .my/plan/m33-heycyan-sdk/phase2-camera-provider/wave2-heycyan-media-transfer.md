# M33 Phase 2 — Wave 2: `HeyCyanMediaTransfer`

**Parent:** [`../phase2-camera-provider.md`](../phase2-camera-provider.md)
**Siblings:** [wave1](wave1-wifi-p2p-http-client.md) · [wave3](wave3-heycyan-camera-provider.md) · [wave4](wave4-camera-manager-integration.md) · [wave5](wave5-latency-benchmarks.md)

> See [sdk-api-reference.md](../sdk-api-reference.md) for the authoritative
> Android SDK type/method names. Transfer-mode entry on Android is the
> two-step sequence
> `LargeDataHandler.GetInstance().GlassesControl(new byte[] { 0x02, 0x01, 0x04 }, cb)`
> followed by `LargeDataHandler.GetInstance().WriteIpToSoc(httpUrl, cb)`,
> and exit is
> `GlassesControl(new byte[] { 0x02, 0x01, 0x09 }, cb)` (reset P2P =
> `0x02 0x01 0x0F`).

## Goal

Build the cross-platform orchestrator that hides the BLE-trigger ↔ Wi-Fi-Direct
↔ HTTP dance behind a clean `IHeyCyanMediaTransfer` interface. This is the
**only** type the camera provider talks to. Its single most important
behavior is **warm transfer mode**: hold the session open across consecutive
captures with a short idle timeout (8 s) so back-to-back frames amortize the
~2-5 s group-formation cost down to 700 ms-1.5 s.

## Steps

1. **Define the cross-platform contract.** Create
   [src/BodyCam/Services/Glasses/HeyCyan/IHeyCyanMediaTransfer.cs](../../../../src/BodyCam/Services/Glasses/HeyCyan/IHeyCyanMediaTransfer.cs):

   ```csharp
   namespace BodyCam.Services.Glasses.HeyCyan;

   public interface IHeyCyanMediaTransfer : IAsyncDisposable
   {
       bool IsWarm { get; }
       Task<IReadOnlyList<HeyCyanMediaEntry>> ListAsync(CancellationToken ct);
       Task<byte[]> DownloadAsync(string fileName, CancellationToken ct);
       Task ExitAsync(CancellationToken ct);
   }

   public sealed record HeyCyanMediaEntry(
       string Name, long Size, DateTimeOffset Timestamp, HeyCyanMediaKind Kind);

   public enum HeyCyanMediaKind { Photo, Video, Audio, Other }
   ```

   And the platform-injected HTTP abstraction
   [src/BodyCam/Services/Glasses/HeyCyan/IHeyCyanHttpClient.cs](../../../../src/BodyCam/Services/Glasses/HeyCyan/IHeyCyanHttpClient.cs):

   ```csharp
   public interface IHeyCyanHttpClient : IAsyncDisposable
   {
       Uri BaseUri { get; }
       Task<string> GetStringAsync(string path, CancellationToken ct);
       Task<byte[]> GetByteArrayAsync(string path, CancellationToken ct);
   }

   public interface IHeyCyanHttpClientFactory
   {
       Task<IHeyCyanHttpClient> CreateAsync(Uri baseUri, CancellationToken ct);
   }
   ```

   On Android the factory wraps the Wave 1 `WiFiP2pHttpClient`. On iOS
   (Phase 6) it wraps `HotspotHttpClient`.

2. **Implement `media.config` parsing.** Create
   [src/BodyCam/Services/Glasses/HeyCyan/MediaConfigParser.cs](../../../../src/BodyCam/Services/Glasses/HeyCyan/MediaConfigParser.cs).
   Per [`Alternative-HeyCyan-App-and-SDK/android/AGENTS.md`](../../../../Alternative-HeyCyan-App-and-SDK/android/AGENTS.md),
   the file is plaintext, one filename per line. Map extension to kind:

   ```csharp
   internal static class MediaConfigParser
   {
       public static IReadOnlyList<HeyCyanMediaEntry> Parse(string raw)
       {
           var lines = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries
                                      | StringSplitOptions.TrimEntries);
           var entries = new List<HeyCyanMediaEntry>(lines.Length);
           foreach (var line in lines)
           {
               var name = line;
               var kind = Path.GetExtension(name).ToLowerInvariant() switch
               {
                   ".jpg" or ".jpeg" => HeyCyanMediaKind.Photo,
                   ".mp4" => HeyCyanMediaKind.Video,
                   ".opus" => HeyCyanMediaKind.Audio,
                   _ => HeyCyanMediaKind.Other,
               };
               // Filenames typically encode a timestamp; if not, parse from
               // a HEAD request later. For now, fall back to "now".
               entries.Add(new HeyCyanMediaEntry(name, Size: -1,
                   Timestamp: TryParseTimestamp(name) ?? DateTimeOffset.UtcNow,
                   Kind: kind));
           }
           return entries;
       }
   }
   ```

3. **Implement `HeyCyanMediaTransfer`.** Place at
   [src/BodyCam/Services/Glasses/HeyCyan/HeyCyanMediaTransfer.cs](../../../../src/BodyCam/Services/Glasses/HeyCyan/HeyCyanMediaTransfer.cs).
   It composes `IHeyCyanGlassesSession` (from M33 Phase 1) and
   `IHeyCyanHttpClientFactory`. Internal state:

   ```csharp
   private readonly IHeyCyanGlassesSession _session;
   private readonly IHeyCyanHttpClientFactory _httpFactory;
   private readonly TimeSpan _warmIdle;          // default 8s, injectable
   private readonly TimeProvider _time;          // for testability
   private readonly SemaphoreSlim _gate = new(1, 1);

   private IHeyCyanHttpClient? _http;
   private CancellationTokenSource? _idleCts;
   ```

4. **Implement `EnsureTransferModeAsync` (the hot path).**

   ```csharp
   private async Task EnsureTransferModeAsync(CancellationToken ct)
   {
       await _gate.WaitAsync(ct);
       try
       {
           _idleCts?.Cancel();           // cancel pending idle teardown
           if (_http is not null) return;

           // Phase 1's session implements the two-step entry:
           //   1) LargeDataHandler.GetInstance().GlassesControl(
           //          new byte[] { 0x02, 0x01, 0x04 }, callback);
           //   2) LargeDataHandler.GetInstance().WriteIpToSoc(httpUrl, callback);
           // and waits for the parsed GlassesDeviceNotifyRsp frame where
           // LoadData[6] == 0x08 (IPv4 octets at [7..10]) on the multiplexed
           // listener (AddOutDeviceListener type=100 or transfer-only type=2).
           // It returns a transfer session whose BaseUrl is
           // http://<glasses-ip>/.
           var transfer = await _session.EnterTransferModeAsync(ct);
           _http = await _httpFactory.CreateAsync(new Uri(transfer.BaseUrl), ct);
       }
       finally { _gate.Release(); }
   }
   ```

5. **Implement `ListAsync` and `DownloadAsync`.** Both call
   `EnsureTransferModeAsync`, then on success call `ScheduleIdleExit()`
   to (re)arm the warm-idle timer:

   ```csharp
   public async Task<byte[]> DownloadAsync(string fileName, CancellationToken ct)
   {
       await EnsureTransferModeAsync(ct);
       var bytes = await _http!.GetByteArrayAsync($"/files/{fileName}", ct);
       ScheduleIdleExit();
       return bytes;
   }

   private void ScheduleIdleExit()
   {
       _idleCts?.Cancel();
       var cts = _idleCts = new CancellationTokenSource();
       _ = Task.Delay(_warmIdle, cts.Token).ContinueWith(t =>
       {
           if (!t.IsCanceled) _ = TeardownAsync(CancellationToken.None);
       }, TaskScheduler.Default);
   }
   ```

6. **Implement `TeardownAsync` and `ExitAsync`.** `TeardownAsync` MUST:
   - Be idempotent (re-entrancy gated by `_gate`).
   - Dispose `_http` (which on Android calls
     `BindProcessToNetwork(null)` per Wave 1).
   - Send the BLE exit
     `LargeDataHandler.GetInstance().GlassesControl(new byte[] { 0x02, 0x01, 0x09 }, cb)`
     via `IHeyCyanGlassesSession.ExitTransferModeAsync` so the
     glasses tear down their P2P group. (For a transient `0x09` error
     during group formation, `IHeyCyanGlassesSession` may instead issue
     `GlassesControl(new byte[] { 0x02, 0x01, 0x0F }, cb)` to reset P2P
     without exiting transfer mode.)
   - Set `_http = null`.
   - Note: callbacks fire on the BLE I/O `HandlerThread`; marshal back
     to the orchestrator's `SynchronizationContext` before resolving the
     `Task` returned by `ExitTransferModeAsync`.

   `ExitAsync` is a public alias for `TeardownAsync`. `DisposeAsync`
   calls `TeardownAsync(CancellationToken.None)`.

7. **Cancellation correctness.** If `ct` is cancelled mid-download,
   propagate the `OperationCanceledException` AFTER calling
   `ScheduleIdleExit()` is **skipped**. The session can stay warm; the
   caller decides whether to retry or call `ExitAsync`.

8. **DI registration.** In
   [src/BodyCam/MauiProgram.cs](../../../../src/BodyCam/MauiProgram.cs):

   ```csharp
   builder.Services.AddSingleton<IHeyCyanMediaTransfer, HeyCyanMediaTransfer>();
   ```

9. **Unit tests.** Create
   [src/BodyCam.Tests/Services/Glasses/HeyCyan/HeyCyanMediaTransferTests.cs](../../../../src/BodyCam.Tests/Services/Glasses/HeyCyan/HeyCyanMediaTransferTests.cs)
   with a `FakeSession` (counts `EnterTransferModeAsync` /
   `ExitTransferModeAsync` calls) and a `FakeHttpClient` (canned
   `media.config` + bytes). Use a `FakeTimeProvider` to advance past
   `_warmIdle` deterministically.

## Verify

- [ ] `ListAsync` returns parsed `HeyCyanMediaEntry` rows with correct
      `Kind` for `.jpg` / `.mp4` / `.opus`.
- [ ] Two consecutive `DownloadAsync` calls within `_warmIdle` invoke
      `IHeyCyanGlassesSession.EnterTransferModeAsync` exactly once.
- [ ] Advancing the fake clock past `_warmIdle` triggers `TeardownAsync`,
      which calls `ExitTransferModeAsync` (BLE
      `LargeDataHandler.GlassesControl(new byte[] { 0x02, 0x01, 0x09 }, cb)`)
      exactly once.
- [ ] A third `DownloadAsync` after the warm window re-enters transfer
      mode (counter == 2).
- [ ] `ExitAsync` is idempotent (calling twice does not double-send the
      BLE exit).
- [ ] `DisposeAsync` cleans up `_idleCts` and `_http`.
- [ ] `MediaConfigParser` handles trailing newlines, blank lines, and
      mixed `\r\n` / `\n` line endings.
- [ ] Cancellation mid-`DownloadAsync` does NOT trigger an idle
      teardown (warm session stays available for the next call).
