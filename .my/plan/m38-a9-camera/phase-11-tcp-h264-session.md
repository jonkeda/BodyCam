# Phase 11 - TCP PPPP/iLnk H.264 Session

**Status:** Planned

## Goal

Add an optional TCP PPPP/iLnk session path for camera variants confirmed by
phase 10 to require TCP and raw H.264 streaming.

This phase should live beside the current UDP/MJPEG `A9Session`; it should not
regress the existing A9/X5 provider.

## Scope

- Connect to the discovered camera endpoint using the selected TCP port.
- Implement the PPPP/iLnk handshake documented by captured packets and
  `cam-reverse` notes.
- Send login credentials.
- Negotiate or store the session key when required by the variant.
- Send the video stream request command.
- Read raw H.264 NAL units from the TCP stream.
- Do not assume RTSP or ONVIF.

## Proposed API

```csharp
public interface IA9H264Session : IAsyncDisposable
{
    Task ConnectAsync(A9DiscoveredCamera camera, CancellationToken cancellationToken);
    IAsyncEnumerable<ReadOnlyMemory<byte>> GetH264NalUnitsAsync(CancellationToken cancellationToken);
    Stream GetH264Stream();
}
```

The exact API can change during implementation, but it should preserve a clear
boundary between packet/session work and frame decoding work.

## Implementation

1. Add `A9TcpH264Session` with explicit state transitions:
   - disconnected
   - connecting
   - handshaking
   - authenticated
   - streaming
   - faulted
2. Add packet builders/parsers for login, session-key negotiation, keepalive,
   and stream request commands.
3. Add a NAL-unit reader that handles start-code framed H.264:
   - `00 00 01`
   - `00 00 00 01`
4. Surface stream faults with actionable exception messages.
5. Keep credentials sourced from the existing A9 settings page.
6. Add fake TCP camera tests that validate handshake order and stream parsing.

## Files

- `src/BodyCam/Services/Camera/A9/A9TcpH264Session.cs`
- `src/BodyCam/Services/Camera/A9/A9TcpProtocol.cs`
- `src/BodyCam/Services/Camera/A9/A9H264NalReader.cs`
- `src/BodyCam.Tests/Services/Camera/A9/A9TcpH264SessionTests.cs`
- `src/BodyCam.Tests/Services/Camera/A9/A9H264NalReaderTests.cs`

## Acceptance Criteria

- TCP/H.264 sessions are used only when discovery/settings select that variant.
- Login, session-key, and stream-request packet behavior is covered by tests.
- H.264 NAL units can be read from a fake TCP stream.
- TCP failure does not affect the current UDP/MJPEG A9 provider.
