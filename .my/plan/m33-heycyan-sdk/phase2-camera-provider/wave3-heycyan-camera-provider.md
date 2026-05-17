# M33 Phase 2 — Wave 3: `HeyCyanCameraProvider`

**Parent:** [`../phase2-camera-provider.md`](../phase2-camera-provider.md)
**Siblings:** [wave1](wave1-wifi-p2p-http-client.md) · [wave2](wave2-heycyan-media-transfer.md) · [wave4](wave4-camera-manager-integration.md) · [wave5](wave5-latency-benchmarks.md)

> See [sdk-api-reference.md](../sdk-api-reference.md) for the authoritative
> Android SDK type/method names. The BLE photo trigger on Android is
> `LargeDataHandler.GetInstance().GlassesControl(new byte[] { 0x02, 0x01, 0x01 }, cb)`
> (start photo mode) and/or
> `new byte[] { 0x02, 0x01, 0x06, 0x02, 0x02 }` (AI photo). Stop photo mode
> is `0x02 0x01 0x0B`. Video and audio capture byte sequences are open
> questions — see Section E of the API reference and treat them as
> hardware-investigation needed.

## Goal

Implement `HeyCyanCameraProvider : ICameraProvider` (M11) — the public
surface that the rest of BodyCam sees. Each `CaptureFrameAsync` call
issues a BLE photo command, waits for the media-count delta, downloads
the new JPG via Wave 2's warm transfer helper, and returns the bytes.
The provider explicitly **does not support live streaming** — these
glasses are not an RTSP/MJPEG camera.

## Steps

1. **Create the provider.** Place at
   [src/BodyCam/Services/Glasses/HeyCyan/HeyCyanCameraProvider.cs](../../../../src/BodyCam/Services/Glasses/HeyCyan/HeyCyanCameraProvider.cs).

   ```csharp
   namespace BodyCam.Services.Glasses.HeyCyan;

   public sealed class HeyCyanCameraProvider : ICameraProvider, IAsyncDisposable
   {
       public string ProviderId => "heycyan-glasses";
       public string DisplayName => "HeyCyan Glasses Camera";

       public bool IsAvailable =>
           _session.State is HeyCyanState.Connected or HeyCyanState.TransferMode;

       private readonly IHeyCyanGlassesSession _session;
       private readonly IHeyCyanMediaTransfer _transfer;
       private readonly ILogger<HeyCyanCameraProvider> _log;

       public HeyCyanCameraProvider(
           IHeyCyanGlassesSession session,
           IHeyCyanMediaTransfer transfer,
           ILogger<HeyCyanCameraProvider> log)
       {
           _session = session;
           _transfer = transfer;
           _log = log;
       }
   }
   ```

2. **Implement `CaptureFrameAsync`.** Five-step flow:

   ```csharp
   public async Task<byte[]> CaptureFrameAsync(CancellationToken ct)
   {
       if (!IsAvailable)
           throw new InvalidOperationException("HeyCyan glasses are not connected.");

       // 1. Snapshot the current photo count so we can detect the new file.
       var beforeCount = _session.LastMediaCount?.Photos ?? 0;
       var beforeNames = await TryListAsync(ct);

       // 2. Trigger BLE photo capture. Phase 1 wraps
       //    LargeDataHandler.GetInstance().GlassesControl(
       //        new byte[] { 0x02, 0x01, 0x01 }, callback)  // start photo mode
       //    or new byte[] { 0x02, 0x01, 0x06, 0x02, 0x02 }   // AI photo trigger.
       _log.LogInformation("HeyCyan: triggering photo capture (current count={Count})", beforeCount);
       await _session.TakePhotoAsync(ct);

       // 3. Wait for MediaCountUpdated (raised by Phase 1 when the glasses
       //    notify a new media count). Bounded to 6 s.
       var newName = await WaitForNewPhotoAsync(beforeCount, beforeNames,
                                                TimeSpan.FromSeconds(6), ct);

       // 4. Fallback if the count notify was missed: enter transfer mode
       //    and pick the newest .jpg by timestamp.
       if (newName is null)
       {
           _log.LogWarning("HeyCyan: media-count notify timed out, falling back to newest entry");
           var entries = await _transfer.ListAsync(ct);
           newName = entries
               .Where(e => e.Kind == HeyCyanMediaKind.Photo)
               .OrderByDescending(e => e.Timestamp)
               .FirstOrDefault()?.Name
               ?? throw new InvalidOperationException("No photo found on glasses.");
       }

       // 5. Download via the warm transfer helper. Do NOT call ExitAsync —
       //    Wave 2's idle timer will tear down once back-to-back captures stop.
       var jpg = await _transfer.DownloadAsync(newName, ct);
       AssertJpegMagic(jpg);
       return jpg;
   }
   ```

3. **Implement `WaitForNewPhotoAsync`.** Subscribe to
   `IHeyCyanGlassesSession.MediaCountUpdated`, complete on the first
   event whose `Photos > beforeCount`, and time out via
   `Task.WhenAny(tcs.Task, Task.Delay(timeout, ct))`. If the session
   already exposes a `NewMediaName` channel, prefer that — but treat
   the event as authoritative for "a new photo exists", and rely on
   the Wave 2 fallback to recover the filename. The underlying notify
   arrives as a `GlassesDeviceNotifyRsp` on the multiplexed listener
   (`LargeDataHandler.AddOutDeviceListener(100, ...)`) where
   `LoadData[6] == 0x05` carries battery/state — the session is
   responsible for marshalling these BLE-thread callbacks onto a known
   `SynchronizationContext` before raising `MediaCountUpdated`.

4. **Implement live-stream stubs.** `ICameraProvider` declares
   start/stop streaming and a `FrameAvailable` event. These hardware
   glasses cannot stream:

   ```csharp
   public Task StartStreamAsync(CancellationToken ct) =>
       throw new NotSupportedException(
           "HeyCyan glasses do not support live streaming. Use CaptureFrameAsync.");

   public Task StopStreamAsync(CancellationToken ct) => Task.CompletedTask;
   public event EventHandler<byte[]>? FrameAvailable; // never raised
   ```

   Document this on the M11 `ICameraProvider` XML doc and in the M11
   docs page (Wave 4 ties this in to user-facing settings).

5. **Validate JPEG magic bytes.** Glasses occasionally return partial
   bodies if the HTTP transfer races with another mode switch. Guard:

   ```csharp
   private static void AssertJpegMagic(byte[] bytes)
   {
       if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8)
           throw new InvalidDataException(
               $"HeyCyan returned non-JPEG bytes (len={bytes.Length}, first2={bytes[0]:X2}{bytes[1]:X2}).");
   }
   ```

6. **`DisposeAsync` semantics.** The provider does NOT own the session
   or the transfer (both are DI singletons), so `DisposeAsync` is a
   no-op:

   ```csharp
   public ValueTask DisposeAsync() => ValueTask.CompletedTask;
   ```

7. **Cancellation correctness.** If `ct` cancels mid-`CaptureFrameAsync`
   AFTER `TakePhotoAsync` but BEFORE the download completes, the photo
   still lands on glasses storage. Do NOT exit transfer mode — leaving
   it warm lets the next `CaptureFrameAsync` retrieve the orphaned file
   via the Wave 2 fallback path.

8. **Unit tests.** Add
   [src/BodyCam.Tests/Services/Glasses/HeyCyan/HeyCyanCameraProviderTests.cs](../../../../src/BodyCam.Tests/Services/Glasses/HeyCyan/HeyCyanCameraProviderTests.cs)
   with a fake session + fake transfer covering:

   ```csharp
   [Fact] public async Task CaptureFrameAsync_ReturnsJpegFromTransferHelper();
   [Fact] public async Task CaptureFrameAsync_WhenMediaCountNotifyTimesOut_FallsBackToNewestEntry();
   [Fact] public async Task CaptureFrameAsync_WhenSessionDisconnected_ThrowsInvalidOperation();
   [Fact] public async Task CaptureFrameAsync_NonJpegBytes_ThrowsInvalidData();
   [Fact] public async Task StartStreamAsync_ThrowsNotSupported();
   ```

9. **DI registration (Android-only for now).** In
   [src/BodyCam/Platforms/Android/MauiProgram.Android.cs](../../../../src/BodyCam/Platforms/Android/MauiProgram.Android.cs):

   ```csharp
   builder.Services.AddSingleton<ICameraProvider, HeyCyanCameraProvider>();
   builder.Services.AddSingleton<HeyCyanCameraProvider>();
   ```

   On platforms without an `IHeyCyanGlassesSession` implementation
   (iOS pre-Phase-6, Windows), the provider is **not** registered.
   Wave 4 wires the M11 `CameraManager` selection rule.

## Verify

- [ ] `CaptureFrameAsync` returns bytes whose `[0..1]` are `FF D8`
      (valid JPEG SOI).
- [ ] On a successful capture, `IHeyCyanGlassesSession.EnterTransferModeAsync`
      is invoked at most once (warm reuse from Wave 2 holds).
- [ ] Two captures issued back-to-back share a single transfer session
      and the second returns within the warm latency target (Wave 5).
- [ ] When `MediaCountUpdated` is suppressed in tests, the fallback
      path lists `media.config` and selects the newest `.jpg`.
- [ ] When `IsAvailable` is false (state `Disconnected`),
      `CaptureFrameAsync` throws `InvalidOperationException`.
- [ ] `StartStreamAsync` throws `NotSupportedException` with a message
      pointing at `CaptureFrameAsync`.
- [ ] `FrameAvailable` is never raised (verified by an unhooked-handler
      assertion in tests).
- [ ] Cancellation mid-download does NOT call `_transfer.ExitAsync` —
      the warm session is preserved for the next attempt.
