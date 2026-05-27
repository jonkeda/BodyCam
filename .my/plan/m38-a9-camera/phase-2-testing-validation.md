# Phase 2 - Testing & Validation

**Status:** Planned

## Goal

Prove the A9 protocol implementation is correct and resilient before relying on
real hardware in the main camera workflow.

## Scope

- Unit-test protocol helpers without network I/O.
- Add a mock UDP camera server for session-level integration tests.
- Add gated real-hardware tests for physical A9/X5 cameras.
- Exercise packet loss, timeout, reconnect, and corrupt-frame behavior.

## Implementation

1. Add `A9Protocol` unit tests:
   - Cipher encrypt/decrypt round trip.
   - Command ID parsing.
   - Packet length framing.
   - Control payload builders.
   - ACK packet builders.
2. Add a mock UDP A9 server:
   - Respond to `LanSearch` with `PunchPkt`.
   - Complete `P2pRdy` and `ConnectUserAck`.
   - Acknowledge `VideoParamSet` and `StartVideo`.
   - Emit segmented JPEG frames.
   - Emit keepalive packets.
3. Add `A9Session` integration tests against the mock server:
   - Successful connect and first frame.
   - Timeout when no `PunchPkt` arrives.
   - Login failure or missing `ConnectUserAck`.
   - Out-of-order frame segment drop.
   - Disconnect event when packet flow stops.
4. Add real-hardware tests gated by environment variables:
   - `A9_E2E=1`.
   - `A9_CAMERA_IP`.
   - Optional `A9_CAMERA_USERNAME` and `A9_CAMERA_PASSWORD`.
5. Capture real-hardware notes in the test output:
   - Firmware/app variant if visible.
   - Network mode: AP or LAN.
   - Average first-frame latency.

## Files

- `src/BodyCam.Tests/Services/Camera/A9/A9ProtocolTests.cs`
- `src/BodyCam.Tests/Services/Camera/A9/A9SessionTests.cs`
- `src/BodyCam.Tests/Services/Camera/A9/FakeA9UdpServer.cs`
- `src/BodyCam.RealTests/A9/A9CameraRealTests.cs`

## Acceptance Criteria

- Protocol tests cover normal and malformed packet inputs.
- Mock-server tests can connect, stream at least one JPEG, and disconnect cleanly.
- Packet loss tests prove corrupt frames are dropped instead of surfaced.
- Real-hardware tests are skipped by default and only run when explicitly enabled.
- Test failures include enough packet/session context to diagnose the failing stage.
- No real-hardware credentials are committed.
