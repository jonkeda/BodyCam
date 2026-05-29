# Phase 0 - A9 Hardware Probe CLI And RealTests

**Status:** Implemented

## Goal

Define a small A9 hardware probe CLI first, then wrap the same probe code in
RealTests. The CLI should let Codex ask the user to switch on one camera, run a
short diagnostic probe suite, and report which connection path the camera
supports before changing protocol code.

No special Codex skill is required. This is a normal human-in-the-loop hardware
flow using a local CLI plus `src/BodyCam.RealTests` for repeatable gated tests.

## 2026-05-28 Hardware Probe Outcome

Phase 0 implementation is in place. The first live probe did not reach the
powered-on camera from the current Windows network:

- Local Wi-Fi was `10.3.81.153/22` on `Exact-WLAN`.
- Candidate hosts were `10.3.80.1`, `192.168.1.1`, `192.168.169.1`, and
  after follow-up variant research, `192.168.4.1`.
- RTSP, HTTP/MJPEG, V720 TCP `6123`, PPPP UDP `32108`, and PPPP UDP `20190`
  all timed out or appeared closed.
- No first frame was captured because no supported protocol was selected.

Saved artifacts:

- High-level log: `./realtests-log.md`
- Report: `./realtests-report-2026-05-28.md`
- JSON probe capture: `./captures/a9-probe-latest.json`

## Why CLI First

A CLI is easier than xUnit for phase 0 because hardware probing is exploratory:

- It can print a live, ordered probe transcript without test-runner noise.
- It can run without env-var ceremony for safe read-only diagnostics.
- It can accept quick options like `--host`, `--protocol`, `--timeout`, and
  `--first-frame`.
- It can return structured JSON for later tests.
- It can be run repeatedly while the user switches Wi-Fi networks or powers a
  camera on/off.

RealTests should still exist, but they should reuse the same probe services once
the CLI has proven the shape of the camera.

## Operator Flow

1. Codex asks the user to switch on exactly one A9 camera.
2. The user confirms whether the computer is connected to:
   - the camera AP network, usually with camera IP `192.168.1.1`
   - the same LAN as the camera
3. Codex runs the A9 probe CLI.
4. The CLI prints a concise diagnostic summary:
   - local network/interface used
   - configured or discovered camera IP
   - protocol probes attempted
   - selected connection path
   - first-frame result when available
5. Codex summarizes the output and updates the relevant phase decision.
6. After the CLI path is stable, Codex runs the A9 RealTests filter to make the
   result repeatable.

## CLI Commands

Default probe:

```powershell
dotnet run --project tools\BodyCam.A9Probe\BodyCam.A9Probe.csproj -- probe
```

Probe a known AP-mode A9/X5 camera:

```powershell
dotnet run --project tools\BodyCam.A9Probe\BodyCam.A9Probe.csproj -- probe `
  --host 192.168.1.1 `
  --first-frame
```

Probe a known V720/Naxclow camera:

```powershell
dotnet run --project tools\BodyCam.A9Probe\BodyCam.A9Probe.csproj -- probe `
  --host 192.168.169.1 `
  --protocol v720-naxclow `
  --first-frame
```

Emit machine-readable output for RealTests or saved diagnostics:

```powershell
dotnet run --project tools\BodyCam.A9Probe\BodyCam.A9Probe.csproj -- probe `
  --json `
  --output .my\plan\m38-a9-camera\captures\a9-probe-latest.json
```

## CLI Output

The CLI should write both a readable transcript and optional JSON:

```text
A9 probe
Local IPv4: 192.168.169.23
Candidate: 192.168.169.1

[rtsp]        closed/timeout
[http-mjpeg]  closed/timeout
[v720]        connected tcp/6123, base info read, jpeg frame received
[pppp-udp]    skipped: v720 selected

Selected: v720-naxclow
Frame: JPEG 18423 bytes
```

The JSON shape should include:

- timestamp
- local interface
- candidate hosts
- probes attempted
- selected protocol
- failure reasons
- frame metadata when available

## Environment Variables

Real hardware tests must skip unless explicitly enabled.

```powershell
$env:A9_E2E = "1"
$env:A9_CAMERA_IP = "192.168.1.1"
$env:A9_CAMERA_USERNAME = "admin"
$env:A9_CAMERA_PASSWORD = "admin"
```

Optional diagnostic mode for discovery/probing without a known IP:

```powershell
$env:A9_E2E = "1"
$env:A9_DISCOVERY_E2E = "1"
```

## Run Command

RealTests should be the second step after the CLI has identified the protocol.

```powershell
dotnet test src\BodyCam.RealTests\BodyCam.RealTests.csproj `
  -f net10.0-windows10.0.19041.0 `
  --filter "FullyQualifiedName~BodyCam.RealTests.A9" `
  --logger "console;verbosity=detailed" `
  --no-restore
```

For the existing single-session test only:

```powershell
dotnet test src\BodyCam.RealTests\BodyCam.RealTests.csproj `
  -f net10.0-windows10.0.19041.0 `
  --filter "FullyQualifiedName~A9Session_ConnectsAndReceivesJpegFrame" `
  --logger "console;verbosity=detailed" `
  --no-restore
```

## RealTest Definitions

### Existing Test

`A9Session_ConnectsAndReceivesJpegFrame`

- Requires `A9_E2E=1`.
- Requires `A9_CAMERA_IP`.
- Uses `A9_CAMERA_USERNAME` / `A9_CAMERA_PASSWORD`, defaulting to `admin`.
- Connects through the current UDP/MJPEG `A9Session`.
- Passes when a JPEG frame is received and starts with `FF D8`.

### Add: Discovery Probe Test

`A9Discovery_DiscoversCameraOrPrintsProbeSummary`

- Requires `A9_E2E=1` and `A9_DISCOVERY_E2E=1`.
- Does not require `A9_CAMERA_IP`.
- Asks the user to power on one camera before running.
- Broadcasts/probes the phase 5 discovery paths:
  - RTSP direct probe candidates
  - HTTP MJPEG direct probe candidates
  - UDP `32108` PPPP/iLnk `LanSearch`
  - UDP `20190` binary PPPP/iLnk discovery
- Prints all candidates and marks the preferred protocol.
- Skips with a clear message if no camera answers.

### Add: Protocol Matrix Test

`A9ProtocolMatrix_DetectsSupportedConnectionPath`

- Requires `A9_E2E=1`.
- Uses `A9_CAMERA_IP` when provided.
- If no IP is provided and `A9_DISCOVERY_E2E=1`, uses discovery output.
- Probes in this order:
  1. RTSP
  2. HTTP MJPEG
  3. V720/Naxclow AP mode on TCP `6123`
  4. PPPP/iLnk UDP/MJPEG
  5. PPPP/iLnk TCP/H.264, only after phase 11 exists
- Writes the chosen protocol to test output.
- Does not fail just because unsupported protocols are closed.
- Fails only when the selected protocol claims to work but cannot connect.

### Add: First Frame Test Per Protocol

`A9SelectedProtocol_ReceivesFirstFrame`

- Requires `A9_E2E=1`.
- Uses the selected protocol from the matrix probe.
- Captures a single frame or stream packet.
- For MJPEG/JPEG paths, verifies a JPEG start marker.
- For H.264 paths, verifies a NAL start code once that path is implemented.

## Safety Rules

- Tests must use short timeouts.
- Tests must never require more than one powered-on A9 camera.
- Tests must not mutate camera settings.
- Tests must not assume internet access.
- Tests must skip when hardware opt-in env vars are missing.
- Tests must print enough diagnostics for Codex to summarize without guessing.

## Files

- `tools/BodyCam.A9Probe/BodyCam.A9Probe.csproj`
- `tools/BodyCam.A9Probe/Program.cs`
- `src/BodyCam/Services/Camera/A9/Probe/A9ProbeRunner.cs`
- `src/BodyCam/Services/Camera/A9/Probe/A9ProbeResult.cs`
- `src/BodyCam.RealTests/A9/A9CameraRealTests.cs`
- `src/BodyCam.RealTests/A9/A9CameraDiscoveryRealTests.cs`
- `src/BodyCam.RealTests/A9/A9ProtocolMatrixRealTests.cs`
- `src/BodyCam.RealTests/A9/V720NaxclowRealTests.cs`
- `src/BodyCam/Services/Camera/A9/A9EndpointProbeService.cs`
- `.my/plan/m38-a9-camera/protocol-variants.md`

## Acceptance Criteria

- Codex can run the A9 probe CLI without hardware opt-in env vars and get a
  readable "no camera found" result instead of a failing test run.
- The CLI can probe a known host and report the selected protocol.
- The CLI can attempt a first-frame capture for the selected protocol.
- The CLI can emit JSON output for saved diagnostics.
- Codex can run the A9 RealTests filter and get skipped tests when no hardware
  opt-in is set.
- With one powered-on camera and env vars set, the tests identify at least one
  reachable protocol or print actionable probe failures.
- Existing `A9Session_ConnectsAndReceivesJpegFrame` remains available for the
  current UDP/MJPEG path.
- CLI and RealTests output are clear enough to decide whether phase 5, 10, 11,
  12, or 14 is the next correct implementation step.
