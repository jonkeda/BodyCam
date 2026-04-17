# M11 Phases 3 & 4 — WiFi / IP Cameras & WiFi Glasses

## Goal

Support network cameras via standard protocols (RTSP, HTTP MJPEG) and
WiFi-connected smart glasses that stream over WiFi-Direct or local network.

---

## Phase 3: IP Camera Provider

### Protocols

| Protocol | How it works | Latency | Common in |
|----------|-------------|---------|-----------|
| **RTSP** | Real Time Streaming Protocol over TCP/UDP. H.264/H.265 video. | 100-300ms | IP cameras, NVRs, WiFi glasses |
| **HTTP MJPEG** | Multipart HTTP response, each part is a JPEG frame. | 200-500ms | Cheap IP cams, ESP32-CAM |
| **ONVIF** | SOAP-based discovery + RTSP streaming. | same as RTSP | Professional IP cameras |
| **WebRTC** | Peer-to-peer, low latency. | <100ms | Modern cameras, some glasses |

### IpCameraProvider

```csharp
namespace BodyCam.Services.Camera;

/// <summary>
/// Camera provider for RTSP or HTTP MJPEG network cameras.
/// </summary>
public class IpCameraProvider : ICameraProvider
{
    private readonly HttpClient _httpClient;
    private byte[]? _latestFrame;
    private CancellationTokenSource? _streamCts;
    private Task? _streamTask;

    public string DisplayName { get; }
    public string ProviderId => $"ip:{_streamUrl}";
    public bool IsAvailable { get; private set; }

    public event EventHandler? Disconnected;

    private readonly string _streamUrl;      // rtsp://... or http://...
    private readonly StreamProtocol _protocol;

    public IpCameraProvider(string streamUrl, StreamProtocol protocol, string displayName)
    {
        _streamUrl = streamUrl;
        _protocol = protocol;
        DisplayName = displayName;
        _httpClient = new HttpClient();
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        _streamCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        _streamTask = _protocol switch
        {
            StreamProtocol.Mjpeg => StreamMjpegAsync(_streamCts.Token),
            StreamProtocol.Rtsp  => StreamRtspAsync(_streamCts.Token),
            _ => throw new NotSupportedException($"Protocol {_protocol} not supported")
        };

        IsAvailable = true;
    }

    public async Task StopAsync()
    {
        _streamCts?.Cancel();
        if (_streamTask is not null)
            await _streamTask;
        IsAvailable = false;
    }

    public Task<byte[]?> CaptureFrameAsync(CancellationToken ct = default)
    {
        // Return the latest frame from the background stream
        return Task.FromResult(_latestFrame);
    }

    // ... StreamMjpegAsync, StreamRtspAsync implementations
}

public enum StreamProtocol
{
    Mjpeg,
    Rtsp
}
```

### HTTP MJPEG Implementation

MJPEG is the simplest protocol — the server sends a continuous HTTP response
where each part is a JPEG frame separated by a boundary string.

```csharp
private async Task StreamMjpegAsync(CancellationToken ct)
{
    try
    {
        using var response = await _httpClient.GetAsync(
            _streamUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        using var stream = await response.Content.ReadAsStreamAsync(ct);

        // Parse multipart boundary from Content-Type header
        var boundary = ParseBoundary(response.Content.Headers.ContentType);

        await foreach (var frame in ReadMjpegFramesAsync(stream, boundary, ct))
        {
            _latestFrame = frame;
        }
    }
    catch (OperationCanceledException) { }
    catch
    {
        IsAvailable = false;
        Disconnected?.Invoke(this, EventArgs.Empty);
    }
}
```

### RTSP Implementation

RTSP requires decoding H.264/H.265 video and extracting individual frames.
This is significantly more complex than MJPEG.

**Options:**

| Library | License | Platform | Notes |
|---------|---------|----------|-------|
| **FFmpeg via FFMediaToolkit** | LGPL | Windows, Android | Full codec support, large binary |
| **RtspClientSharp** | MIT | Cross-platform | Pure C# RTSP client, H.264 only |
| **LibVLCSharp** | LGPL | Cross-platform | VLC under the hood, heavy |

**Recommended: RtspClientSharp** (MIT license, pure .NET, ~50KB)

```csharp
private async Task StreamRtspAsync(CancellationToken ct)
{
    var connectionParameters = new ConnectionParameters(new Uri(_streamUrl));
    using var rtspClient = new RtspClient(connectionParameters);

    rtspClient.FrameReceived += (sender, frame) =>
    {
        if (frame is RawJpegFrame jpegFrame)
        {
            _latestFrame = jpegFrame.FrameBytes;
        }
        else if (frame is RawH264Frame h264Frame)
        {
            // Decode H.264 → JPEG (requires decoder)
            _latestFrame = DecodeH264ToJpeg(h264Frame);
        }
    };

    await rtspClient.ConnectAsync(ct);
    await rtspClient.ReceiveAsync(ct);
}
```

---

## Phase 4: WiFi Glasses

### Chinese WiFi Glasses — Protocol Landscape

Most Chinese smart glasses with WiFi cameras fall into these categories:

| Category | Protocol | Discovery | Examples |
|----------|----------|-----------|----------|
| **WiFi-Direct AP** | Glasses create WiFi hotspot, phone connects. RTSP or MJPEG stream on known port. | Connect to glasses SSID | Most TKYUAN models |
| **App-relayed** | Proprietary app receives stream, must reverse-engineer | N/A | Some models |
| **Local network** | Glasses join home WiFi, stream on mDNS-discoverable address | mDNS/Bonjour | Higher-end models |

### WifiGlassesCameraProvider

This provider extends `IpCameraProvider` with WiFi-Direct discovery and
glasses-specific connection logic.

```csharp
namespace BodyCam.Services.Camera;

/// <summary>
/// Camera provider for WiFi-connected smart glasses.
/// Handles WiFi-Direct connection and stream discovery.
/// </summary>
public class WifiGlassesCameraProvider : ICameraProvider
{
    private IpCameraProvider? _innerProvider;

    public string DisplayName => _glassesName ?? "WiFi Glasses";
    public string ProviderId => "wifi-glasses";
    public bool IsAvailable => _innerProvider?.IsAvailable ?? false;

    public event EventHandler? Disconnected;

    private string? _glassesName;
    private string? _streamUrl;

    public async Task StartAsync(CancellationToken ct = default)
    {
        // 1. Discover glasses WiFi-Direct AP or mDNS service
        _streamUrl = await DiscoverStreamUrlAsync(ct);
        if (_streamUrl is null) return;

        // 2. Determine protocol from URL
        var protocol = _streamUrl.StartsWith("rtsp://")
            ? StreamProtocol.Rtsp
            : StreamProtocol.Mjpeg;

        // 3. Create inner IP provider with discovered URL
        _innerProvider = new IpCameraProvider(_streamUrl, protocol, DisplayName);
        _innerProvider.Disconnected += (_, _) => Disconnected?.Invoke(this, EventArgs.Empty);
        await _innerProvider.StartAsync(ct);
    }

    public Task<byte[]?> CaptureFrameAsync(CancellationToken ct = default)
        => _innerProvider?.CaptureFrameAsync(ct) ?? Task.FromResult<byte[]?>(null);

    // ... discovery logic
}
```

### WiFi-Direct Discovery

#### Windows
```csharp
// Use Windows.Devices.WiFiDirect
var watcher = WiFiDirectAdvertisementPublisher.CreateWatcher();
// or scan for known SSIDs matching glasses naming pattern
```

#### Android
```csharp
// Use Android.Net.Wifi.P2p.WifiP2pManager
// Discover peers, connect to glasses, then resolve IP address
```

### Investigation Protocol (when glasses arrive)

When a new glasses model arrives, follow this checklist to determine the
streaming protocol:

1. **Factory reset glasses**
2. **Check if glasses create a WiFi hotspot**
   - Look for new SSIDs on the phone/laptop WiFi scanner
   - Connect to the glasses hotspot
   - Scan for open ports: `nmap -sT <glasses-ip> -p 80,554,1935,8080,8554`
   - Port 554 → likely RTSP
   - Port 80/8080 → likely HTTP MJPEG
3. **Try common stream URLs:**
   - `rtsp://<ip>:554/live` or `rtsp://<ip>:554/stream`
   - `http://<ip>:80/mjpeg` or `http://<ip>:8080/video`
   - `http://<ip>/cgi-bin/snapshot.cgi`
4. **Install manufacturer app, capture traffic:**
   - Use mitmproxy or Wireshark to capture HTTP/RTSP traffic
   - Note the stream URL and any authentication
5. **Document findings** per model in `docs/glasses/`

### Per-Model Configuration

Store known glasses profiles so users don't need to discover streams manually:

```json
// glasses-profiles.json (embedded resource)
[
  {
    "model": "TKYUAN BT5.3 WiFi",
    "ssidPattern": "TKYUAN_*",
    "defaultStreamUrl": "rtsp://{ip}:554/live",
    "protocol": "rtsp",
    "defaultIp": "192.168.1.1"
  },
  {
    "model": "Generic WiFi Glasses",
    "ssidPattern": null,
    "defaultStreamUrl": null,
    "protocol": null,
    "notes": "Manual configuration required"
  }
]
```

---

## Settings

New settings for WiFi cameras:

```csharp
// ISettingsService additions
string? WifiCameraStreamUrl { get; set; }      // Manual RTSP/MJPEG URL entry
string? WifiCameraProtocol { get; set; }       // "rtsp" or "mjpeg"
string? GlassesProfile { get; set; }           // Selected glasses profile name
```

Settings UI:
- **Camera picker** shows discovered WiFi cameras alongside other sources
- **Manual URL entry** for advanced users (RTSP/MJPEG URL field)
- **Glasses profile selector** for known models
- **Scan button** to discover WiFi cameras on the local network
