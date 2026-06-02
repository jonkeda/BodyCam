# M33 Phase 2 — Camera Provider (file-based snapshot)

> See [sdk-api-reference.md](sdk-api-reference.md) for the authoritative
> Android SDK type/method names. All Android API references below use the
> real `Com.Oudmon.Ble.Base.*` binding (C# style).

Implement `HeyCyanCameraProvider` as an `ICameraProvider` (M11) that captures a
single still frame by issuing a BLE photo command, switching the glasses into
Wi-Fi transfer mode, and pulling the resulting JPG over HTTP. The glasses are
**not a live camera** — there is no RTSP/MJPEG stream to subscribe to. Every
frame is a discrete BLE-triggered file transfer.

**Depends on:** M33 Phase 1 (Android SDK binding & `IHeyCyanGlassesSession`),
M11 Phase 1 (`ICameraProvider` abstraction).

---

## Background

Confirmed protocol (see
[`Alternative-HeyCyan-App-and-SDK/WIFI_TRANSFER_ARCHITECTURE.md`](../../../Alternative-HeyCyan-App-and-SDK/WIFI_TRANSFER_ARCHITECTURE.md),
[`android/AGENTS.md`](../../../Alternative-HeyCyan-App-and-SDK/android/AGENTS.md),
[`MainActivity.kt`](../../../Alternative-HeyCyan-App-and-SDK/android/CyanBridge/app/src/main/java/com/fersaiyan/cyanbridge/MainActivity.kt),
[`WifiP2pManagerSingleton.kt`](../../../Alternative-HeyCyan-App-and-SDK/android/CyanBridge/app/src/main/java/com/fersaiyan/cyanbridge/ui/wifi/p2p/WifiP2pManagerSingleton.kt)):

1. Phone sends BLE photo command (`TakePhotoAsync` from Phase 1, which wraps
   `LargeDataHandler.GetInstance().GlassesControl(new byte[] { 0x02, 0x01, 0x01 }, callback)`
   — start photo mode — and/or
   `new byte[] { 0x02, 0x01, 0x06, 0x02, 0x02 }` for AI photo trigger).
   Video and audio start/stop byte sequences were **not located** in the
   working CyanBridge sample — see `sdk-api-reference.md` Section E open
   questions; hardware-investigation needed.
2. Glasses capture the still and update the media-count notify frame
   (`GlassesDeviceNotifyRsp.LoadData[6] == 0x05` battery / new filename is
   announced via the multiplexed device-notify channel).
3. Phone enters transfer mode (TWO-STEP):
   1. `LargeDataHandler.GetInstance().GlassesControl(new byte[] { 0x02, 0x01, 0x04 }, callback);`
   2. `LargeDataHandler.GetInstance().WriteIpToSoc(httpUrl, callback);`
4. Glasses bring up a Wi-Fi-Direct group and notify the phone with their IPv4
   address via the parsed `GlassesDeviceNotifyRsp` where `LoadData[6] == 0x08`
   and IPv4 octets occupy `LoadData[7..10]`. (Notify `LoadData[6] == 0x09`
   payload `0xFF` is noisy/non-fatal — log and ignore.) Listen on both:
   - `LargeDataHandler.GetInstance().AddOutDeviceListener(100, listener)` —
     multiplexed (battery, IP, button, errors)
   - `LargeDataHandler.GetInstance().AddOutDeviceListener(2, listener)` —
     transfer-mode-only (IP + P2P error)
5. Phone joins the P2P group, **binds the process to the P2P network**
   (Samsung multi-network routing), and HTTP-`GET`s
   `http://<glasses-ip>/files/media.config` to enumerate filenames.
6. Phone HTTP-`GET`s `http://<glasses-ip>/files/<name>` for the newest `.jpg`.
7. Phone exits transfer mode:
   `LargeDataHandler.GetInstance().GlassesControl(new byte[] { 0x02, 0x01, 0x09 }, callback);`
   To reset the P2P state machine without exiting (e.g. after a transient
   `0x09` error), use
   `GlassesControl(new byte[] { 0x02, 0x01, 0x0F }, callback)`.

> All `ILargeDataResponse<T>.ParseData` callbacks fire on the BLE I/O
> `HandlerThread` owned by `BleOperateManager`. Marshal back to a known
> `SynchronizationContext` before raising MAUI-facing events.

### Critical Gotchas

- **Do not use `WifiP2pInfo.groupOwnerAddress`.** On these glasses the phone is
  the group owner (`192.168.49.1`); the glasses are a P2P client. Always use
  the BLE-reported IPv4 from the `GlassesDeviceNotifyRsp` frame where
  `LoadData[6] == 0x08` (octets at `LoadData[7..10]`).
- **`ConnectivityManager.bindProcessToNetwork(p2pNetwork)` is mandatory** on
  Samsung (and other multi-network) Android devices, otherwise HTTP requests
  route over the cellular network and time out.
- **Cleartext HTTP is required.** Add a `network_security_config.xml` entry
  permitting cleartext to `192.168.49.0/24` (or all hosts in debug builds).
- **Permissions:** `NEARBY_WIFI_DEVICES` (API 33+, with
  `android:usesPermissionFlags="neverForLocation"`), and
  `ACCESS_FINE_LOCATION` for peer discovery on older API levels.
- **Latency.** End-to-end capture-to-bytes is ~2–5 s (BLE round-trip + Wi-Fi
  Direct group formation + HTTP). Compare to <50 ms for a phone camera —
  callers must not assume real-time. Document this in M11 docs.

### Optimization: Warm Transfer Mode

Group formation is the dominant cost. Keep the transfer mode session warm
across consecutive `CaptureFrameAsync` calls and only exit after a short idle
timeout (e.g. 8 s). Subsequent captures inside the warm window drop to
~700 ms–1.5 s.

---

## Wave 1: `WiFiP2pHttpClient` (Android)

Android-specific helper that owns the P2P group lifecycle and provides a
process-bound `HttpClient` rooted at the glasses IP.

```csharp
// Platforms/Android/HeyCyan/WiFiP2pHttpClient.cs
namespace BodyCam.Services.Glasses.HeyCyan.Android;

internal sealed class WiFiP2pHttpClient : IAsyncDisposable
{
    private readonly WifiP2pManager _manager;
    private readonly WifiP2pManager.Channel _channel;
    private readonly ConnectivityManager _connectivity;
    private readonly ILogger<WiFiP2pHttpClient> _log;

    private Network? _p2pNetwork;
    private HttpClient? _http;

    public string? GlassesIp { get; private set; }
    public Uri? BaseUri => GlassesIp is null ? null : new Uri($"http://{GlassesIp}/");

    public async Task ConnectAsync(string glassesIp, CancellationToken ct)
    {
        // 1. Discover + form/join P2P group via _manager.CreateGroup / Connect.
        //    Group is initiated by glasses on transfer-mode entry; phone joins.
        // 2. Resolve the P2P Network handle from ConnectivityManager.
        // 3. Bind process to that network so HttpClient routes over P2P.
        _p2pNetwork = await ResolveP2pNetworkAsync(ct);
        _connectivity.BindProcessToNetwork(_p2pNetwork);

        GlassesIp = glassesIp; // BLE-reported, NOT groupOwnerAddress
        _http = new HttpClient
        {
            BaseAddress = BaseUri,
            // HTTP server on glasses is slow under load; allow generous read time.
            Timeout = TimeSpan.FromSeconds(20),
        };
    }

    public Task<string> GetStringAsync(string path, CancellationToken ct) =>
        _http!.GetStringAsync(path, ct);

    public Task<byte[]> GetByteArrayAsync(string path, CancellationToken ct) =>
        _http!.GetByteArrayAsync(path, ct);

    public async ValueTask DisposeAsync()
    {
        _http?.Dispose();
        _http = null;
        _connectivity.BindProcessToNetwork(null);
        // P2P group teardown happens when glasses exit transfer mode.
    }
}
```

iOS will provide a `HotspotHttpClient` with the same shape (using
`NEHotspotConfiguration`) — that work lands in Phase 6.

### Verify
- [ ] `ConnectAsync` returns only after `bindProcessToNetwork` succeeds.
- [ ] HTTP `GET` of `http://<ip>/files/media.config` returns 200 with non-empty body.
- [ ] `Dispose` unbinds the process from the P2P network (cellular restored).
- [ ] `network_security_config.xml` permits cleartext to the glasses IP.

---

## Wave 2: `HeyCyanMediaTransfer` (cross-platform helper)

Cross-platform orchestrator that wraps enter/exit + media.config parse +
download. Implementations are injected per platform; the camera provider only
talks to this interface.

```csharp
// Services/Glasses/HeyCyan/HeyCyanMediaTransfer.cs
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

internal sealed class HeyCyanMediaTransfer : IHeyCyanMediaTransfer
{
    private readonly IHeyCyanGlassesSession _session;
    private readonly IHeyCyanHttpClientFactory _httpFactory; // Android or iOS impl
    private readonly TimeSpan _warmIdle = TimeSpan.FromSeconds(8);
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IHeyCyanHttpClient? _http;
    private CancellationTokenSource? _idleCts;

    public bool IsWarm => _http is not null;

    public async Task<IReadOnlyList<HeyCyanMediaEntry>> ListAsync(CancellationToken ct)
    {
        await EnsureTransferModeAsync(ct);
        var raw = await _http!.GetStringAsync("/files/media.config", ct);
        return MediaConfigParser.Parse(raw);
    }

    public async Task<byte[]> DownloadAsync(string fileName, CancellationToken ct)
    {
        await EnsureTransferModeAsync(ct);
        var bytes = await _http!.GetByteArrayAsync($"/files/{fileName}", ct);
        ScheduleIdleExit();
        return bytes;
    }

    public Task ExitAsync(CancellationToken ct) => TeardownAsync(ct);

    private async Task EnsureTransferModeAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            _idleCts?.Cancel();
            if (_http is not null) return;

            var transfer = await _session.EnterTransferModeAsync(ct);
            _http = await _httpFactory.CreateAsync(transfer.BaseUrl, ct);
        }
        finally { _gate.Release(); }
    }

    private void ScheduleIdleExit()
    {
        _idleCts?.Cancel();
        var cts = _idleCts = new CancellationTokenSource();
        _ = Task.Delay(_warmIdle, cts.Token)
                .ContinueWith(t =>
                {
                    if (!t.IsCanceled) _ = TeardownAsync(CancellationToken.None);
                }, TaskScheduler.Default);
    }

    private async Task TeardownAsync(CancellationToken ct) { /* dispose _http, exit BLE */ }

    public async ValueTask DisposeAsync() => await TeardownAsync(CancellationToken.None);
}
```

### Verify
- [ ] `ListAsync` returns parsed `HeyCyanMediaEntry` rows from `media.config`.
- [ ] Two consecutive `DownloadAsync` calls within `_warmIdle` reuse the same
      transfer session (verified via test counter on `EnterTransferModeAsync`).
- [ ] After `_warmIdle` of inactivity, transfer mode is exited automatically.
- [ ] `ExitAsync` is idempotent.

---

## Wave 3: `HeyCyanCameraProvider`

```csharp
// Services/Glasses/HeyCyan/HeyCyanCameraProvider.cs
namespace BodyCam.Services.Glasses.HeyCyan;

public sealed class HeyCyanCameraProvider : ICameraProvider, IAsyncDisposable
{
    public string ProviderId => "heycyan-glasses";
    public string DisplayName => "HeyCyan Glasses Camera";
    public bool IsAvailable => _session.State is HeyCyanState.Connected
                                              or HeyCyanState.TransferMode;

    private readonly IHeyCyanGlassesSession _session;
    private readonly IHeyCyanMediaTransfer _transfer;
    private readonly ILogger<HeyCyanCameraProvider> _log;

    private int _knownPhotoCount;

    public async Task<byte[]> CaptureFrameAsync(CancellationToken ct)
    {
        // 1. Snapshot current media count so we can detect the new file.
        var before = await GetPhotoCountAsync(ct);

        // 2. Trigger BLE photo capture.
        await _session.TakePhotoAsync(ct);

        // 3. Wait for media-count delta (or new filename) — Phase 1 raises
        //    MediaCountUpdated when the glasses notify us.
        var newName = await WaitForNewPhotoAsync(before, TimeSpan.FromSeconds(6), ct);

        // 4. List and download via the warm transfer helper.
        if (newName is null)
        {
            // Fallback: enter transfer mode and pick the newest entry by timestamp.
            var entries = await _transfer.ListAsync(ct);
            newName = entries
                .Where(e => e.Kind == HeyCyanMediaKind.Photo)
                .OrderByDescending(e => e.Timestamp)
                .First().Name;
        }

        var jpg = await _transfer.DownloadAsync(newName, ct);
        // 5. Do NOT exit transfer mode here — let the warm idle timer handle it.
        return jpg;
    }

    public ValueTask DisposeAsync() => _transfer.DisposeAsync();

    // Live-stream methods are not supported on this hardware.
    public Task StartStreamAsync(CancellationToken ct) =>
        throw new NotSupportedException("HeyCyan glasses are not a live camera.");
    public Task StopStreamAsync(CancellationToken ct) => Task.CompletedTask;
    public event EventHandler<byte[]>? FrameAvailable; // never raised
}
```

### Verify
- [ ] `CaptureFrameAsync` returns a valid JPEG (`bytes[0..1] == FF D8`).
- [ ] After capture the session returns to `Connected` (not stuck in
      `TransferMode`) once the warm timer elapses.
- [ ] Cancellation mid-flow exits transfer mode cleanly.
- [ ] `StartStreamAsync` throws `NotSupportedException` (documented in M11).

---

## Wave 4: Integration with M11 `CameraManager` & Settings

- Register `HeyCyanCameraProvider` in DI on Android (and iOS once Phase 6
  lands). On platforms without the binding it must not be registered.
- M11 `CameraManager` already supports multiple providers; add a
  selection rule: prefer `heycyan-glasses` when an active
  `IHeyCyanGlassesSession` is `Connected`, otherwise fall back to the phone
  camera. Auto-fallback on session `Disconnected`.
- Settings page: add a "Glasses Camera" entry showing connection state and a
  "Test Capture" button that calls `CaptureFrameAsync` and renders the
  resulting JPG with the measured latency.

### Verify
- [ ] `CameraManager.ActiveProvider` switches to `heycyan-glasses` on connect.
- [ ] Disconnecting the glasses reverts to phone camera within one frame request.
- [ ] Settings "Test Capture" succeeds end-to-end on real hardware.

---

## Wave 5: Latency Benchmarks & Warm-Mode Tests

Tests live in `BodyCam.Tests` (with a fake transfer / session) and
`BodyCam.RealTests` (against real glasses, opt-in).

```csharp
// BodyCam.Tests/Services/Glasses/HeyCyan/HeyCyanCameraProviderTests.cs
[Fact]
public async Task CaptureFrameAsync_ReturnsJpegFromTransferHelper() { /* fake */ }

[Fact]
public async Task CaptureFrameAsync_TwiceWithinWarmWindow_EntersTransferModeOnce()
{
    // Arrange fake session that counts EnterTransferModeAsync calls.
    // Act: two captures back-to-back.
    // Assert: counter == 1.
}

[Fact]
public async Task CaptureFrameAsync_AfterWarmIdleElapsed_ReentersTransferMode()
{
    // Use a virtual clock; advance past _warmIdle between captures.
}
```

```csharp
// BodyCam.RealTests/HeyCyanCameraLatencyTests.cs
[Fact, Trait("Category", "RealHardware")]
public async Task CaptureFrameAsync_ColdLatency_IsUnder6Seconds() { /* … */ }

[Fact, Trait("Category", "RealHardware")]
public async Task CaptureFrameAsync_WarmLatency_IsUnder2Seconds() { /* … */ }
```

### Verify
- [ ] Unit tests cover warm reuse, idle expiry, and cancellation.
- [ ] Real-hardware cold-capture benchmark recorded (target ≤ 6 s).
- [ ] Real-hardware warm-capture benchmark recorded (target ≤ 2 s).
- [ ] Latency numbers documented in M11 docs alongside phone-camera baseline.

---

## Phase Exit Checklist

- [ ] `WiFiP2pHttpClient` (Android) connects, binds process to P2P network,
      and downloads `media.config`.
- [ ] `IHeyCyanMediaTransfer` + `HeyCyanMediaTransfer` orchestrate
      enter/list/download/exit with warm idle timeout.
- [ ] `HeyCyanCameraProvider.CaptureFrameAsync` returns a valid JPEG on
      Android against real hardware.
- [ ] iOS `HotspotHttpClient` interface defined; iOS implementation stubbed
      with `PlatformNotSupportedException` until Phase 6.
- [ ] Provider integrated with M11 `CameraManager`; fallback to phone camera
      on disconnect verified.
- [ ] Settings page shows status + "Test Capture" works.
- [ ] Cold/warm latency benchmarks recorded and documented in M11 docs.
- [ ] Permissions (`NEARBY_WIFI_DEVICES`, location) and
      `network_security_config.xml` cleartext entry merged.
