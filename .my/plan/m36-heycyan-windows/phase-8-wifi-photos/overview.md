# Phase 6 — WiFi Photo Transfer from HeyCyan Glasses

**Status:** Proposed  
**Depends on:** Phase 2 (BLE session — **complete**), Phase 5 (WiFi joining — **proposed**)  
**Sibling phases:** [Phase 1](../phase-1-ble-discovery/overview.md), [Phase 2](../phase-2-windows-ble/overview.md), [Phase 3](../phase-3-windows-wifi/overview.md), [Phase 4](../phase-4-integration/overview.md), [Phase 5](../phase-5-wifi-joining/overview.md)

---

## Goal

Implement the full end-to-end pipeline to download photos from HeyCyan
glasses on Windows: BLE command → WiFi join → HTTP file listing → photo
download → local save → WiFi restore. This is the **first real media
transfer** on Windows.

---

## What already exists

| Component | File | Status |
|---|---|---|
| BLE `EnterTransferMode` command | `HeyCyanCommands.EnterTransferMode()` → `{0x02, 0x01, 0x04}` | ✅ Implemented |
| BLE `ExitTransferMode` command | `HeyCyanCommands.ExitTransferMode()` → `{0x02, 0x01, 0x09}` | ✅ Implemented |
| BLE IP notify frame parsing | `HeyCyanFrameParser.TryParseTransferIp()` — frame `[6]==0x08`, IPv4 `[7..10]` | ✅ Implemented |
| P2P error classification | `HeyCyanFrameParser.ClassifyP2pError()` — frame `[6]==0x09` | ✅ Implemented |
| `WindowsHeyCyanGlassesSession.EnterTransferModeAsync()` | Sends BLE cmd, waits for IP notify, returns `HeyCyanTransferSession` | ✅ Implemented |
| `HeyCyanMediaTransfer` | `ListAsync()` → `GET /files/media.config`, `DownloadAsync()` → `GET /files/{name}` | ✅ Shared cross-platform |
| `MediaConfigParser.Parse()` | Parses `media.config` → `IReadOnlyList<HeyCyanMediaEntry>` | ✅ Shared |
| `WindowsHeyCyanHttpClientFactory` | Simple `HttpClient` wrapper (no network binding needed on Windows) | ✅ Implemented |
| `IHeyCyanMediaTransfer` interface | `IsWarm`, `ListAsync`, `DownloadAsync`, `OpenAsync`, `ExitAsync` | ✅ Shared |
| WiFi joining on Windows | **Nothing** — glasses IP is unreachable after transfer mode entry | ❌ Missing |
| SSID/password discovery from BLE | **Nothing** — IP is parsed but SSID is not | ❌ Missing |
| End-to-end photo download | **Nothing** — no test or UI exercises the full pipeline | ❌ Missing |

---

## What other platforms do

### iOS (reference implementation)

```
1. BLE: openWifiWithMode:QCOperatorDeviceModeTransfer → success:(ssid, password)
2. WiFi: NEHotspotConfiguration(ssid, password ?: "123456789", isWEP:NO)
3. IP discovery: probe 192.168.43.1, 192.168.4.1, 192.168.1.1, ...
4. HTTP: GET http://{ip}/manifest.json → file list
5. HTTP: GET http://{ip}/files/{filename} → download each file
6. BLE: exit transfer mode
7. WiFi: remove hotspot configuration → auto-reconnect to previous network
```

### Android

```
1. BLE: SDK enters transfer mode internally
2. WiFi: WifiP2pManager creates WiFi Direct group (auto-negotiated, no SSID/password)
3. Network binding: ConnectivityManager.BindProcessToNetwork() to force traffic over P2P
4. HTTP: same /files/media.config + /files/{name} protocol
5. BLE: exit transfer mode
6. WiFi: disconnect P2P group
```

### Windows (this phase)

```
1. BLE: EnterTransferModeAsync() — already implemented, returns IP
2. ❓ SSID discovery — parse BLE frame OR scan available WiFi networks
3. WiFi: WiFiAdapter.ConnectAsync(network, credential) — needs implementation
4. HTTP: HeyCyanMediaTransfer.ListAsync() + DownloadAsync() — already exists
5. BLE: ExitTransferModeAsync() — already implemented
6. WiFi: WiFiAdapter.Disconnect() — needs implementation
```

---

## Protocol details

### Transfer mode activation sequence

```
Phone/PC                          Glasses
   |                                  |
   |-- BLE Write: 0x02 0x01 0x04 --> |  (enter transfer mode)
   |                                  |  (glasses start WiFi hotspot)
   |<-- BLE Notify: type 0x08 -----  |  (IP address: bytes 7-10)
   |                                  |
   |-- WiFi: join glasses hotspot --> |
   |                                  |
   |-- HTTP GET /files/media.config ->|  (file listing)
   |<-- JSON: [{name, size, ts}, ...] |
   |                                  |
   |-- HTTP GET /files/IMG_0001.jpg ->|  (download photo)
   |<-- JPEG bytes ---------------    |
   |                                  |
   |-- BLE Write: 0x02 0x01 0x09 --> |  (exit transfer mode)
   |                                  |  (glasses tear down hotspot)
   |-- WiFi: restore previous -----  |
   |                                  |
```

### media.config format

The glasses serve `GET /files/media.config` which returns a text-based
manifest parsed by `MediaConfigParser.Parse()`. Each line contains
comma-separated fields: filename, size, timestamp, type. This yields
`HeyCyanMediaEntry(Name, Size, Timestamp, Kind)` where `Kind` is
`Photo`, `Video`, `Audio`, or `Other`.

### Known IP ranges

The glasses' hotspot typically uses one of these IPs (from iOS IP
discovery code):

| IP | Common for |
|---|---|
| `192.168.43.1` | Android hotspot default |
| `192.168.4.1` | Alternative hotspot range |
| `192.168.1.1` | Router default |

The IP is extracted from the BLE notify frame, so probing is not needed
on Windows — `TryParseTransferIp()` gives us the exact IP.

---

## Implementation plan

### 6.1 — SSID/password discovery

**Goal:** Determine how to discover the glasses' WiFi SSID and password on
Windows.

**Investigation tasks:**

1. **Check if BLE notify carries SSID.** The iOS SDK's `openWifiWithMode`
   returns `(ssid, password)` in the success callback. The current
   `WindowsHeyCyanGlassesSession.OnCharacteristicValueChanged` dispatches
   `0x08` frames but only extracts IP. Check if additional bytes in the
   `0x08` frame carry SSID/password, or if a separate frame type carries
   them.

2. **Scan WiFi and match by SSID pattern.** If BLE doesn't carry the SSID,
   the glasses' SSID may follow a known pattern (e.g., contains device name
   or "HeyCyan" or MAC suffix). After entering transfer mode, scan available
   WiFi networks and look for a match.

3. **Use default password.** iOS uses `"123456789"` as fallback. This is
   likely universal across HeyCyan models.

**Implementation in `WindowsHeyCyanGlassesSession`:**

```csharp
// Option A: Parse SSID from BLE frame (if present)
// In OnCharacteristicValueChanged, check for SSID data after the IP bytes

// Option B: Scan WiFi for glasses hotspot
public async Task<(string Ssid, string Password)?> DiscoverGlassesWiFiAsync(
    CancellationToken ct)
{
    var adapters = await WiFiAdapter.FindAllAdaptersAsync().AsTask(ct);
    var adapter = adapters.FirstOrDefault()
        ?? throw new InvalidOperationException("No WiFi adapter");

    await adapter.ScanAsync().AsTask(ct);

    // Match by known patterns: device name, "HeyCyan", MAC suffix
    var candidates = adapter.NetworkReport.AvailableNetworks
        .Where(n => IsLikelyGlassesHotspot(n.Ssid))
        .ToList();

    if (candidates.Count == 0) return null;

    return (candidates[0].Ssid, "123456789"); // default password
}
```

**Deliverable:** Working SSID/password discovery — either from BLE frame
parsing or WiFi scan heuristics.

---

### 6.2 — Windows WiFi manager

**Goal:** Create `WindowsGlassesWiFiManager` that joins/leaves the glasses'
WiFi hotspot.

**File:** `src/BodyCam/Platforms/Windows/HeyCyan/WindowsGlassesWiFiManager.cs`

```csharp
using Windows.Devices.WiFi;
using Windows.Security.Credentials;

namespace BodyCam.Platforms.Windows.HeyCyan;

internal sealed class WindowsGlassesWiFiManager
{
    private WiFiAdapter? _adapter;
    private readonly ILogger<WindowsGlassesWiFiManager> _log;

    public WindowsGlassesWiFiManager(ILogger<WindowsGlassesWiFiManager> log)
    {
        _log = log;
    }

    /// <summary>
    /// Join the glasses' WiFi hotspot.
    /// </summary>
    public async Task JoinAsync(string ssid, string password, CancellationToken ct)
    {
        var adapters = await WiFiAdapter.FindAllAdaptersAsync().AsTask(ct);
        _adapter = adapters.FirstOrDefault()
            ?? throw new InvalidOperationException("No WiFi adapter found");

        await _adapter.ScanAsync().AsTask(ct);

        var network = _adapter.NetworkReport.AvailableNetworks
            .FirstOrDefault(n => n.Ssid == ssid)
            ?? throw new InvalidOperationException(
                $"Glasses WiFi '{ssid}' not found in scan results");

        var credential = new PasswordCredential { Password = password };
        var result = await _adapter.ConnectAsync(
            network, WiFiReconnectionKind.Manual, credential).AsTask(ct);

        if (result.ConnectionStatus != WiFiConnectionStatus.Success)
            throw new InvalidOperationException(
                $"Failed to join glasses WiFi: {result.ConnectionStatus}");

        _log.LogInformation("Joined glasses WiFi '{Ssid}'", ssid);
    }

    /// <summary>
    /// Leave the glasses' WiFi (Windows auto-reconnects to previous network).
    /// </summary>
    public void Leave()
    {
        _adapter?.Disconnect();
        _log.LogInformation("Left glasses WiFi");
    }
}
```

**Manifest capability required:** `wifiControl` — should already be declared
from Phase 4 integration.

**Deliverable:** `WindowsGlassesWiFiManager` with `JoinAsync` / `Leave`.

---

### 6.3 — Wire WiFi joining into transfer mode flow

**Goal:** Integrate WiFi joining into `EnterTransferModeAsync` so the glasses'
HTTP endpoint is reachable before `HeyCyanMediaTransfer` makes requests.

**Changes to `WindowsHeyCyanGlassesSession.EnterTransferModeAsync`:**

```csharp
public async Task<HeyCyanTransferSession> EnterTransferModeAsync(CancellationToken ct)
{
    // 1. Send BLE command (existing)
    await SendCommandAsync(HeyCyanCommands.EnterTransferMode(), ct);

    // 2. Wait for IP notify frame (existing)
    var frame = await WaitForNotifyAsync(0x08, TimeSpan.FromSeconds(15), ct);
    if (!HeyCyanFrameParser.TryParseTransferIp(frame, out var ip))
        throw new InvalidOperationException("Failed to parse transfer IP");

    // 3. NEW: Join glasses WiFi
    var (ssid, password) = await DiscoverGlassesWiFiAsync(ct);
    await _wifiManager.JoinAsync(ssid, password, ct);

    // 4. Return transfer session (existing)
    SetState(HeyCyanState.TransferMode);
    return new HeyCyanTransferSession($"http://{ip}", Array.Empty<string>());
}
```

**Changes to `ExitTransferModeAsync`:**

```csharp
public async Task ExitTransferModeAsync(CancellationToken ct)
{
    // 1. Send BLE exit command (existing)
    await SendCommandAsync(HeyCyanCommands.ExitTransferMode(), ct);

    // 2. NEW: Leave glasses WiFi
    _wifiManager.Leave();

    SetState(HeyCyanState.Connected);
}
```

**Deliverable:** `EnterTransferModeAsync` joins WiFi before returning,
`ExitTransferModeAsync` restores WiFi on exit.

---

### 6.4 — End-to-end photo download

**Goal:** Verify the full pipeline works: enter transfer mode → list media →
download photos → exit.

This step exercises the existing `HeyCyanMediaTransfer` code which is already
cross-platform. The only thing that was missing was WiFi connectivity (6.2–6.3).

**Expected flow:**

```csharp
// 1. Transfer is already wired to session via DI
var files = await _transfer.ListAsync(ct);        // GET /files/media.config
var photos = files.Where(f => f.Kind == RecordedMediaKind.Photo);

foreach (var photo in photos)
{
    var bytes = await _transfer.DownloadAsync(photo.Name, ct);  // GET /files/{name}
    // Save to local storage
    await File.WriteAllBytesAsync(localPath, bytes, ct);
}

await _transfer.ExitAsync(ct);  // BLE exit + WiFi leave
```

**Deliverable:** Successful photo download from glasses to PC, verified with
a real hardware test (see 6.6).

---

### 6.5 — DI registration

**Goal:** Register `WindowsGlassesWiFiManager` in the Windows DI container
and inject it into `WindowsHeyCyanGlassesSession`.

**File:** `src/BodyCam/Platforms/Windows/MauiProgram.Windows.cs` (or wherever
the `#elif WINDOWS` DI registrations live)

```csharp
#elif WINDOWS
services.AddSingleton<WindowsGlassesWiFiManager>();
services.AddSingleton<IHeyCyanGlassesSession>(sp =>
    new WindowsHeyCyanGlassesSession(
        sp.GetRequiredService<ILogger<WindowsHeyCyanGlassesSession>>(),
        sp.GetRequiredService<WindowsBluetoothEnumerator>(),
        sp.GetRequiredService<WindowsBluetoothOutputEnumerator>(),
        sp.GetRequiredService<WindowsGlassesWiFiManager>()));  // NEW param
services.AddSingleton<IHeyCyanHttpClientFactory, WindowsHeyCyanHttpClientFactory>();
#endif
```

**Deliverable:** WiFi manager injected into session, `HeyCyanMediaTransfer`
can make HTTP requests when in transfer mode.

---

### 6.6 — Real hardware tests

**Goal:** Comprehensive real-hardware tests for the WiFi photo transfer
pipeline.

**File:** `src/BodyCam.RealTests/Services/Glasses/HeyCyan/WindowsWiFiTransferTests.cs`

See [Real tests design](#real-tests-design) below.

---

## Real tests design

### Test fixture changes

The existing `WindowsHeyCyanRealFixture` uses `NullMediaTransfer`. For WiFi
transfer tests, we need a real `HeyCyanMediaTransfer` backed by the real
`WindowsHeyCyanHttpClientFactory` and `WindowsGlassesWiFiManager`.

**New fixture method in `WindowsHeyCyanRealFixture`:**

```csharp
/// <summary>
/// Create a fixture with real media transfer support (WiFi + HTTP).
/// </summary>
public static async Task<WindowsHeyCyanRealFixture> CreateWithTransferAsync()
{
    // Same as CreateAsync() but with:
    // - WindowsGlassesWiFiManager (real WiFi)
    // - WindowsHeyCyanHttpClientFactory (real HTTP)
    // - HeyCyanMediaTransfer (real transfer)
    // instead of NullMediaTransfer
}
```

### Test class

```
File: src/BodyCam.RealTests/Services/Glasses/HeyCyan/WindowsWiFiTransferTests.cs

[Trait("Category", "RealWiFiTransfer")]
public sealed class WindowsWiFiTransferTests : IAsyncLifetime
```

**Category:** `RealWiFiTransfer` — separate from `RealConnection` because
these tests take longer (WiFi join/leave cycles) and require the glasses to
have stored media.

**Run command:**
```
$env:BODYCAM_REAL_HEYCYAN="1"
$env:BODYCAM_REAL_HEYCYAN_MAC="D8:79:B8:7F:E6:C9"
dotnet test src/BodyCam.RealTests -f net10.0-windows10.0.19041.0 \
    --filter "Category=RealWiFiTransfer" -v normal
```

### Tests

#### 6.6.1 — `EnterTransferMode_ReceivesIpAddress`

Verifies the BLE transfer mode command triggers an IP notify frame.

```csharp
[SkippableFact]
public async Task EnterTransferMode_ReceivesIpAddress()
{
    Skip.IfNot(RealEnabled);

    var session = await _fixture.EnterTransferModeAsync(CancellationToken.None);

    _output.WriteLine($"Transfer IP: {session.BaseUrl}");

    session.BaseUrl.Should().NotBeNullOrEmpty();
    session.BaseUrl.Should().StartWith("http://");

    // Extract IP and verify it's a valid private address
    var uri = new Uri(session.BaseUrl);
    var ip = IPAddress.Parse(uri.Host);
    ip.AddressFamily.Should().Be(AddressFamily.InterNetwork);

    // Clean up
    await _fixture.ExitTransferModeAsync(CancellationToken.None);
}
```

#### 6.6.2 — `JoinGlassesWiFi_NetworkIsReachable`

Verifies WiFi joining succeeds and the glasses' IP is pingable.

```csharp
[SkippableFact]
public async Task JoinGlassesWiFi_NetworkIsReachable()
{
    Skip.IfNot(RealEnabled);

    var session = await _fixture.EnterTransferModeAsync(CancellationToken.None);
    var uri = new Uri(session.BaseUrl);

    // Verify IP is reachable (HTTP GET should not throw)
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    var reachable = false;
    try
    {
        // The root may not serve anything, but TCP connect should succeed
        await http.GetAsync($"http://{uri.Host}/", CancellationToken.None);
        reachable = true;
    }
    catch (HttpRequestException)
    {
        // 404 is fine — means TCP connected but no root handler
        reachable = true;
    }
    catch (TaskCanceledException)
    {
        reachable = false;
    }

    _output.WriteLine($"Glasses IP {uri.Host} reachable: {reachable}");
    reachable.Should().BeTrue("glasses IP should be reachable after WiFi join");

    await _fixture.ExitTransferModeAsync(CancellationToken.None);
}
```

#### 6.6.3 — `ListMedia_ReturnsNonEmptyList`

Verifies the media manifest endpoint returns parseable file entries.

**Prerequisite:** Take at least one photo on the glasses before running.

```csharp
[SkippableFact]
public async Task ListMedia_ReturnsNonEmptyList()
{
    Skip.IfNot(RealEnabled);

    var files = await _transfer.ListAsync(CancellationToken.None);

    _output.WriteLine($"Media files on glasses: {files.Count}");
    foreach (var f in files)
        _output.WriteLine($"  {f.Name} ({f.Kind}, {f.Size} bytes, {f.Timestamp})");

    files.Should().NotBeEmpty("glasses should have at least one media file — " +
        "take a photo on the glasses before running this test");
}
```

#### 6.6.4 — `ListMedia_ContainsAtLeastOnePhoto`

Verifies at least one entry has `Kind == Photo`.

```csharp
[SkippableFact]
public async Task ListMedia_ContainsAtLeastOnePhoto()
{
    Skip.IfNot(RealEnabled);

    var files = await _transfer.ListAsync(CancellationToken.None);

    files.Should().Contain(f => f.Kind == RecordedMediaKind.Photo,
        "glasses should have at least one photo");
}
```

#### 6.6.5 — `DownloadPhoto_ReturnsValidJpeg`

Downloads the first photo and validates JPEG SOI/EOI markers.

```csharp
[SkippableFact]
public async Task DownloadPhoto_ReturnsValidJpeg()
{
    Skip.IfNot(RealEnabled);

    var files = await _transfer.ListAsync(CancellationToken.None);
    var photo = files.FirstOrDefault(f => f.Kind == RecordedMediaKind.Photo);
    Skip.If(photo == null, "No photos on glasses");

    _output.WriteLine($"Downloading {photo.Name} ({photo.Size} bytes)...");
    var sw = Stopwatch.StartNew();
    var bytes = await _transfer.DownloadAsync(photo.Name, CancellationToken.None);
    sw.Stop();

    _output.WriteLine($"Downloaded {bytes.Length} bytes in {sw.ElapsedMilliseconds} ms");

    bytes.Should().NotBeEmpty();
    bytes.Should().HaveCountGreaterThan(100, "photo should be more than 100 bytes");

    // Validate JPEG SOI marker (0xFF 0xD8)
    bytes[0].Should().Be(0xFF);
    bytes[1].Should().Be(0xD8);

    // Validate JPEG EOI marker (0xFF 0xD9) at end
    bytes[^2].Should().Be(0xFF);
    bytes[^1].Should().Be(0xD9);
}
```

#### 6.6.6 — `DownloadPhoto_SizeMatchesManifest`

Verifies the downloaded file size matches the manifest entry.

```csharp
[SkippableFact]
public async Task DownloadPhoto_SizeMatchesManifest()
{
    Skip.IfNot(RealEnabled);

    var files = await _transfer.ListAsync(CancellationToken.None);
    var photo = files.FirstOrDefault(f => f.Kind == RecordedMediaKind.Photo);
    Skip.If(photo == null, "No photos on glasses");

    var bytes = await _transfer.DownloadAsync(photo.Name, CancellationToken.None);

    _output.WriteLine($"Manifest size: {photo.Size}, actual: {bytes.Length}");
    bytes.Length.Should().Be((int)photo.Size,
        "downloaded file size should match manifest entry");
}
```

#### 6.6.7 — `DownloadAllPhotos_AllReturnValidJpeg`

Downloads every photo in the manifest and validates each one.

```csharp
[SkippableFact]
public async Task DownloadAllPhotos_AllReturnValidJpeg()
{
    Skip.IfNot(RealEnabled);

    var files = await _transfer.ListAsync(CancellationToken.None);
    var photos = files.Where(f => f.Kind == RecordedMediaKind.Photo).ToList();
    Skip.If(photos.Count == 0, "No photos on glasses");

    _output.WriteLine($"Downloading {photos.Count} photos...");

    foreach (var photo in photos)
    {
        var bytes = await _transfer.DownloadAsync(photo.Name, CancellationToken.None);

        _output.WriteLine($"  {photo.Name}: {bytes.Length} bytes");

        bytes.Should().NotBeEmpty($"{photo.Name} should not be empty");
        bytes[0].Should().Be(0xFF, $"{photo.Name} should start with JPEG SOI");
        bytes[1].Should().Be(0xD8, $"{photo.Name} should start with JPEG SOI");
    }

    _output.WriteLine($"All {photos.Count} photos downloaded successfully");
}
```

#### 6.6.8 — `WarmTransfer_SecondListIsFaster`

Verifies the warm transfer session reuse is faster than cold entry.

```csharp
[SkippableFact]
public async Task WarmTransfer_SecondListIsFaster()
{
    Skip.IfNot(RealEnabled);

    // Cold: first ListAsync triggers EnterTransferModeAsync + WiFi join
    var swCold = Stopwatch.StartNew();
    var files1 = await _transfer.ListAsync(CancellationToken.None);
    swCold.Stop();

    // Warm: second ListAsync reuses existing session (no WiFi join)
    var swWarm = Stopwatch.StartNew();
    var files2 = await _transfer.ListAsync(CancellationToken.None);
    swWarm.Stop();

    _output.WriteLine($"Cold list: {swCold.ElapsedMilliseconds} ms ({files1.Count} files)");
    _output.WriteLine($"Warm list: {swWarm.ElapsedMilliseconds} ms ({files2.Count} files)");

    files1.Count.Should().Be(files2.Count, "same files both times");
    swWarm.Elapsed.Should().BeLessThan(swCold.Elapsed,
        "warm transfer should be faster than cold");
}
```

#### 6.6.9 — `ExitTransferMode_RestoresConnectedState`

Verifies the session returns to `Connected` after exiting transfer mode.

```csharp
[SkippableFact]
public async Task ExitTransferMode_RestoresConnectedState()
{
    Skip.IfNot(RealEnabled);

    // Enter transfer mode
    await _transfer.ListAsync(CancellationToken.None);
    _fixture.Session.State.Should().Be(HeyCyanState.TransferMode);

    // Exit
    await _transfer.ExitAsync(CancellationToken.None);

    _output.WriteLine($"State after exit: {_fixture.Session.State}");
    _fixture.Session.State.Should().Be(HeyCyanState.Connected,
        "should return to Connected after exit transfer mode");
}
```

#### 6.6.10 — `TransferMode_TakePhotoThenDownload_RoundTrip`

Full round-trip: take a photo via BLE, enter transfer mode, download it.

```csharp
[SkippableFact]
public async Task TransferMode_TakePhotoThenDownload_RoundTrip()
{
    Skip.IfNot(RealEnabled);

    // Get initial media count
    var countBefore = await _fixture.Session.GetMediaCountAsync(CancellationToken.None);
    _output.WriteLine($"Photos before: {countBefore.Photos}");

    // Take a photo via BLE
    await _fixture.Session.TakePhotoAsync(CancellationToken.None);
    await Task.Delay(TimeSpan.FromSeconds(3)); // wait for glasses to save

    // Get updated count
    var countAfter = await _fixture.Session.GetMediaCountAsync(CancellationToken.None);
    _output.WriteLine($"Photos after: {countAfter.Photos}");
    countAfter.Photos.Should().BeGreaterThan(countBefore.Photos,
        "photo count should increase after taking a photo");

    // Enter transfer mode and download
    var files = await _transfer.ListAsync(CancellationToken.None);
    var latestPhoto = files
        .Where(f => f.Kind == RecordedMediaKind.Photo)
        .OrderByDescending(f => f.Timestamp)
        .First();

    _output.WriteLine($"Downloading latest photo: {latestPhoto.Name}");
    var bytes = await _transfer.DownloadAsync(latestPhoto.Name, CancellationToken.None);

    bytes.Should().NotBeEmpty();
    bytes[0].Should().Be(0xFF);
    bytes[1].Should().Be(0xD8);
    _output.WriteLine($"Downloaded {bytes.Length} bytes — valid JPEG");

    // Save to TestResults for manual inspection
    var savePath = Path.Combine(
        Path.GetDirectoryName(typeof(WindowsWiFiTransferTests).Assembly.Location)!,
        "..", "..", "..", "..", "..",
        "TestResults", "wifi-transfer", latestPhoto.Name);
    Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
    File.WriteAllBytes(savePath, bytes);
    _output.WriteLine($"Saved to {savePath}");

    await _transfer.ExitAsync(CancellationToken.None);
}
```

#### 6.6.11 — `DownloadPhoto_SaveToLocalFile_Succeeds`

Verifies the photo can be saved to disk and re-read as valid JPEG.

```csharp
[SkippableFact]
public async Task DownloadPhoto_SaveToLocalFile_Succeeds()
{
    Skip.IfNot(RealEnabled);

    var files = await _transfer.ListAsync(CancellationToken.None);
    var photo = files.FirstOrDefault(f => f.Kind == RecordedMediaKind.Photo);
    Skip.If(photo == null, "No photos on glasses");

    var bytes = await _transfer.DownloadAsync(photo.Name, CancellationToken.None);

    var savePath = Path.Combine(Path.GetTempPath(), "bodycam-test", photo.Name);
    Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
    File.WriteAllBytes(savePath, bytes);

    _output.WriteLine($"Saved to {savePath}");

    // Re-read and validate
    var readBack = File.ReadAllBytes(savePath);
    readBack.Length.Should().Be(bytes.Length);
    readBack[0].Should().Be(0xFF);
    readBack[1].Should().Be(0xD8);

    // Cleanup
    File.Delete(savePath);
}
```

#### 6.6.12 — `TransferLatency_ColdEntry_IsUnder15Seconds`

Performance gate: full cold transfer (BLE + WiFi join + HTTP list) under 15s.

```csharp
[SkippableFact]
public async Task TransferLatency_ColdEntry_IsUnder15Seconds()
{
    Skip.IfNot(RealEnabled);

    // Force cold path
    try { await _transfer.ExitAsync(CancellationToken.None); } catch { }
    await Task.Delay(TimeSpan.FromSeconds(10));

    var sw = Stopwatch.StartNew();
    var files = await _transfer.ListAsync(CancellationToken.None);
    sw.Stop();

    _output.WriteLine($"Cold transfer entry + list: {sw.ElapsedMilliseconds} ms");
    _output.WriteLine($"Files: {files.Count}");

    sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(15),
        "cold transfer mode entry + list should complete within 15s");

    await _transfer.ExitAsync(CancellationToken.None);
}
```

---

## Acceptance criteria

- [ ] 6.1: SSID/password discovery works (BLE frame or WiFi scan)
- [ ] 6.2: `WindowsGlassesWiFiManager` joins/leaves glasses WiFi
- [ ] 6.3: `EnterTransferModeAsync` joins WiFi before returning
- [ ] 6.4: `HeyCyanMediaTransfer.ListAsync()` returns files from glasses
- [ ] 6.4: `HeyCyanMediaTransfer.DownloadAsync()` returns valid JPEG bytes
- [ ] 6.5: DI registration includes WiFi manager
- [ ] 6.6: All 12 real hardware tests pass with glasses connected
- [ ] WiFi is restored to previous network after transfer exits

---

## Risks and mitigations

| Risk | Mitigation |
|---|---|
| WiFi SSID not discoverable from BLE frames | Fall back to WiFi scan + pattern matching (device name / "HeyCyan" / MAC suffix) |
| `WiFiAdapter` API requires `wifiControl` capability | Already declared in manifest from Phase 4 |
| Glasses password differs from `"123456789"` | Check BLE frame for password; fall back to default; make configurable |
| WiFi join takes too long (>10s) | Set timeout, retry once, log detailed diagnostics |
| Original WiFi not restored after disconnect | `WiFiAdapter.Disconnect()` + Windows auto-reconnect to known networks |
| Glasses have no stored photos | Tests use `Skip.If(photos.Count == 0)` with clear instructions |
| P2P error (frame type `0x09`) | Already handled by `ClassifyP2pError()` — surface in test output |

---

## Dependencies

```
Phase 2 (BLE session)  ──┐
                          ├──> Phase 6 (this)
Phase 5 (WiFi joining) ──┘
```

Phase 5 is still "Proposed" — this phase incorporates WiFi joining as
step 6.2. If Phase 5 is implemented first, step 6.2 would use its
`WindowsGlassesWiFiManager` directly instead of creating a new one.
