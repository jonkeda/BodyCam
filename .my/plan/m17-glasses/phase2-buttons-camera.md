# M17 Phase 2 — Buttons & Camera

Implement glasses button provider (GATT or AVRCP — based on Phase 1
investigation) and camera provider (WiFi-Direct RTSP/MJPEG). Both plug into
the M14 and M11 abstractions.

**Depends on:** Phase 1 (investigation report determines protocols),
M14 Phase 1 (button abstraction), M11 Phase 1 (camera abstraction).

---

## Wave 1: Glasses Button Provider

The exact implementation depends on the investigation report from Phase 1.
Two likely paths:

### Path A: GATT Custom Button Provider

If the glasses expose button events via a custom GATT characteristic:

```csharp
// Platforms/Windows/GattButtonProvider.cs
public class GattButtonProvider : IButtonInputProvider
{
    public string ProviderId => "glasses-gatt";
    public string DisplayName => "Glasses Button (GATT)";
    public bool IsAvailable => _characteristic != null;

    public event EventHandler<RawButtonEvent>? ButtonEvent;

    private GattCharacteristic? _characteristic;

    public async Task StartAsync(CancellationToken ct = default)
    {
        // Connect to glasses BLE device
        // Subscribe to button characteristic notifications
        // UUID determined from investigation report

        _characteristic.ValueChanged += (_, args) =>
        {
            var data = args.CharacteristicValue;
            var eventType = ParseButtonEvent(data);
            ButtonEvent?.Invoke(this, new RawButtonEvent(
                ProviderId, eventType, DateTimeOffset.UtcNow));
        };

        await _characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
            GattClientCharacteristicConfigurationDescriptorValue.Notify);
    }

    private static ButtonEventType ParseButtonEvent(IBuffer data)
    {
        // Parse based on investigation findings
        // Likely: 0x01 = press, 0x00 = release
        var bytes = data.ToArray();
        return bytes[0] switch
        {
            0x01 => ButtonEventType.Pressed,
            0x00 => ButtonEventType.Released,
            _ => ButtonEventType.Pressed
        };
    }
}
```

### Path B: AVRCP / Media Button Provider

If the glasses send standard media key events (AVRCP profile):

```csharp
// Platforms/Windows/AvrcpButtonProvider.cs
public class AvrcpButtonProvider : IButtonInputProvider
{
    public string ProviderId => "glasses-avrcp";
    public string DisplayName => "Glasses Button (Media)";
    public bool IsAvailable => true; // AVRCP events come via system

    public event EventHandler<RawButtonEvent>? ButtonEvent;

    // On Windows: intercept SystemMediaTransportControls.ButtonPressed
    // On Android: intercept MediaSession callbacks
}
```

### Gesture Mapping

Both paths feed into the existing `GestureRecognizer` (M14 Phase 1) which
handles tap/double-tap/long-press detection, and `ActionMap` which routes
gestures to BodyCam actions.

Default glasses button mapping:

| Gesture | Action |
|---------|--------|
| Single tap | Push-to-talk (start/stop listening) |
| Double tap | Capture photo + describe scene |
| Long press | Start/stop full session |

### Tests
```csharp
public class GattButtonProviderTests
{
    [Fact]
    public void ProviderId_IsGlassesGatt()
    {
        var provider = new GattButtonProvider();
        provider.ProviderId.Should().Be("glasses-gatt");
    }
}
```

### Verify
- [ ] Button provider compiles (path A or B)
- [ ] Button events fire on physical press
- [ ] GestureRecognizer handles tap/double/long
- [ ] ActionMap routes to correct BodyCam actions
- [ ] Tests pass

---

## Wave 2: WiFi Glasses Camera Provider (Windows)

Based on investigation, glasses likely expose camera via WiFi-Direct with
RTSP or HTTP MJPEG stream.

### WifiGlassesCameraProvider

```csharp
// Services/Camera/WifiGlassesCameraProvider.cs
public class WifiGlassesCameraProvider : ICameraProvider
{
    public string ProviderId => "wifi-glasses";
    public string DisplayName => "Glasses Camera (WiFi)";
    public bool IsAvailable => _streamUrl != null;
    public bool IsCapturing { get; private set; }

    public event EventHandler<byte[]>? FrameAvailable;
    public event EventHandler? Disconnected;

    private string? _streamUrl;
    private HttpClient? _httpClient;
    private CancellationTokenSource? _cts;

    /// <summary>
    /// Set the stream URL after WiFi-Direct connection is established.
    /// </summary>
    public void Configure(string streamUrl)
    {
        _streamUrl = streamUrl;
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_streamUrl == null) throw new InvalidOperationException("No stream URL configured");

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        IsCapturing = true;

        _ = CaptureLoopAsync(_cts.Token);
    }

    private async Task CaptureLoopAsync(CancellationToken ct)
    {
        try
        {
            if (_streamUrl!.StartsWith("rtsp://"))
                await CaptureRtspAsync(ct);
            else
                await CaptureMjpegAsync(ct);
        }
        catch (OperationCanceledException) { }
        catch
        {
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            IsCapturing = false;
        }
    }

    private async Task CaptureMjpegAsync(CancellationToken ct)
    {
        // HTTP MJPEG: GET stream, parse multipart boundaries, extract JPEG frames
        _httpClient = new HttpClient();
        using var response = await _httpClient.GetAsync(_streamUrl,
            HttpCompletionOption.ResponseHeadersRead, ct);
        using var stream = await response.Content.ReadAsStreamAsync(ct);

        // Parse MJPEG boundaries: --boundary\r\nContent-Type: image/jpeg\r\n\r\n
        var parser = new MjpegStreamParser(stream);
        await foreach (var frame in parser.ReadFramesAsync(ct))
        {
            FrameAvailable?.Invoke(this, frame);
        }
    }

    private async Task CaptureRtspAsync(CancellationToken ct)
    {
        // RTSP: Use lightweight RTSP client library
        // Decode H.264 frames → JPEG snapshots
        // More complex — may need FFmpeg interop or managed RTSP library
    }

    public async Task<byte[]?> CapturePhotoAsync(CancellationToken ct = default)
    {
        // If streaming, return last frame
        // If not streaming, do a single HTTP GET for snapshot
        if (_streamUrl?.Contains("snapshot") == true || _streamUrl?.Contains("photo") == true)
        {
            _httpClient ??= new HttpClient();
            return await _httpClient.GetByteArrayAsync(_streamUrl, ct);
        }

        // Start stream briefly, capture one frame, stop
        var tcs = new TaskCompletionSource<byte[]>();
        void OnFrame(object? s, byte[] data) { tcs.TrySetResult(data); }
        FrameAvailable += OnFrame;

        await StartAsync(ct);
        var frame = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        await StopAsync(ct);

        FrameAvailable -= OnFrame;
        return frame;
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        _cts?.Cancel();
        _httpClient?.Dispose();
        _httpClient = null;
        IsCapturing = false;
        return Task.CompletedTask;
    }
}
```

### WiFi-Direct Connection

```csharp
// Services/Glasses/WifiDirectService.cs
public class WifiDirectService
{
    /// <summary>
    /// Connect to glasses WiFi-Direct group and return the stream URL.
    /// </summary>
    public async Task<string?> ConnectAndDiscoverStreamAsync(CancellationToken ct)
    {
        // 1. WiFi-Direct discovery (Windows.Devices.WiFiDirect)
        // 2. Connect to glasses group
        // 3. Get glasses IP address
        // 4. Probe for stream: try known URLs
        //    - http://<ip>:8080/video
        //    - rtsp://<ip>:554/stream
        //    - http://<ip>/cgi-bin/snapshot.cgi
        // 5. Return working URL

        var probeUrls = new[]
        {
            $"http://{glassesIp}:8080/video",
            $"rtsp://{glassesIp}:554/stream",
            $"http://{glassesIp}/stream",
        };

        foreach (var url in probeUrls)
        {
            if (await ProbeUrlAsync(url, ct))
                return url;
        }

        return null; // Camera not accessible via known protocols
    }
}
```

### Tests
```csharp
public class WifiGlassesCameraProviderTests
{
    [Fact]
    public void ProviderId_IsWifiGlasses()
    {
        var provider = new WifiGlassesCameraProvider();
        provider.ProviderId.Should().Be("wifi-glasses");
    }

    [Fact]
    public void IsAvailable_WhenNotConfigured_IsFalse()
    {
        var provider = new WifiGlassesCameraProvider();
        provider.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public void Configure_SetsAvailable()
    {
        var provider = new WifiGlassesCameraProvider();
        provider.Configure("http://192.168.1.100:8080/video");
        provider.IsAvailable.Should().BeTrue();
    }
}
```

### Verify
- [ ] MJPEG stream parser works
- [ ] Frames fire via `FrameAvailable` event
- [ ] CapturePhotoAsync returns a single frame
- [ ] WiFi-Direct connection established
- [ ] Stream URL discovery finds camera
- [ ] Tests pass

---

## Wave 3: Android Camera & Button Providers

Port the camera and button providers to Android.

### Android Camera
```csharp
// Same WifiGlassesCameraProvider works cross-platform (HTTP/RTSP are standard)
// WiFi-Direct on Android uses WifiP2pManager
```

### Android Button
```csharp
// If GATT: use Android.Bluetooth.BluetoothGatt
// If AVRCP: intercept via MediaSession
```

### Verify
- [ ] Android WiFi-Direct connects to glasses
- [ ] Camera stream works on Android
- [ ] Button events fire on Android
- [ ] All tests pass
- [ ] Build succeeds on both platforms
