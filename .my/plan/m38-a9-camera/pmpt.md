# Dev Phase: A9 Camera Discovery & Connection (.NET)

## Objective

Implement a .NET library that can discover, connect to, and stream video from A9 mini WiFi cameras using the reverse‑engineered iLnk/PPPP protocol.

## Scope

You will:

1. Implement LAN discovery for A9 cameras using UDP broadcast.
2. Implement the PPPP/iLnk handshake over TCP.
3. Extract and decode the raw H.264 stream.
4. Provide a clean .NET API surface for MAUI, WPF, or console apps.

## Requirements

### 1. Discovery

- Send UDP broadcast packets to ports:
  - 32108 (JSON discovery)
  - 20190 (binary discovery)
- Listen for responses containing:
  - UID
  - IP address
  - Port
- Provide a `DiscoverAsync()` method returning a list of devices.

### 2. PPPP/iLnk Session

- Implement the PPPP handshake as documented in cam‑reverse.
- Support:
  - Login packet
  - Session key negotiation
  - Stream request command
- Expose a `ConnectAsync(device)` method returning a session object.

### 3. Video Stream

- Read raw H.264 NAL units from the TCP stream.
- Provide a callback or channel for decoded frames.
- Do NOT assume RTSP or ONVIF (A9 does not support them).

### 4. Decoding

- Integrate with:
  - FFmpeg.AutoGen (preferred)
  - OR LibVLCSharp as fallback
- Provide:
  - `IAsyncEnumerable<VideoFrame>` or
  - `Stream GetH264Stream()`

### 5. API Surface

Create a clean, developer‑friendly API:

```csharp
var devices = await A9Camera.DiscoverAsync();
var cam = await A9Camera.ConnectAsync(devices.First());
await foreach (var frame in cam.GetFramesAsync())
{
    // render frame
}
```
