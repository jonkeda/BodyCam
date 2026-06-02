# Phase 4f - P2P Sequence And Android Routing

## Goal

Turn the proven official-app media path into the C# probe path without relying
on the HeyCyan app:

- use the media import transfer command, not the realtime preview command;
- form Wi-Fi Direct with the glasses peer only;
- bind BodyCam to the Android P2P network;
- read `/files/media.config` from the glasses media server;
- then download the newest image and video.

## Current Sequence

The best C# sequence now matches the observed media import flow:

1. Start Wi-Fi Direct peer discovery before the BLE transfer command.
2. Send HeyCyan media transfer command `02 01 04`.
3. Poll BLE `02 03` for the transfer IP.
4. Normalize the SDK IP text bug from `49.183.0.0` to `192.168.49.183`.
5. Connect only to likely glasses P2P peers such as `M01`, `QC`, `O_`, or
   `Cyan`.
6. Bind the process to the Android network with route `192.168.49.0/24`.
7. Probe `http://192.168.49.183/files/media.config`.

## Findings

- `02 01 04` is the media import/file transfer command used by the alternative
  CyanBridge flow.
- `02 01 14 01` forms a P2P path for realtime preview/RTSP, but it did not
  expose the HTTP media server in our C# probe run.
- The C# probe must not fall back to arbitrary Wi-Fi Direct peers. A previous
  run connected to a Samsung TV because the peer list fallback was too broad.
- The latest safe peer selection correctly connected to
  `M01 Pro_D879B87FE6C9/60:c2:2a:1a:b6:1b`.
- The phone became group owner at `192.168.49.1`.
- Android reported a P2P network with a `192.168.49.0/24` route.
- The remaining blocker is lower-level Android routing: `Network.OpenConnection`
  to `192.168.49.183` and `192.168.49.200` returned `ENONET` even after the
  route was visible.

## Implementation Change

`WiFiP2pHttpClient` now retries this routing edge in two ways:

- it waits longer after binding to the P2P network before probing
  `media.config`;
- if Android throws `ENONET`, it refreshes the P2P network binding and retries
  the HTTP request through the process-bound default `URL.openConnection()`
  path.

This keeps the known-good BLE command and peer selection stable while testing
whether the failure is a stale Android `Network` object rather than a wrong
HeyCyan protocol step.

## Validation

Pass criteria:

- BodyCam probe creates the P2P group with the `M01 Pro` peer;
- `media.config` returns a valid listing through C#;
- the probe downloads at least one JPEG and one MP4;
- saved files validate by signature/dimensions, not only by content type.

Fail criteria:

- P2P forms with the correct peer and route but every HTTP probe still returns
  `ENONET`, connect timeout, or connection refused.

If this phase fails, the next likely step is to compare a live shell `curl`
during the C#-formed P2P hold window and decide whether the route problem is
inside BodyCam's Android binding path or whether the glasses never starts the
HTTP server for this exact C# sequence.

## Result - Passed

The final 2026-05-31 run passed after two corrections:

- the probe generated fresh media before entering transfer mode;
- video stop was corrected from broad stop-mode `02 01 0b` to the
  CyanBridge video stop command `02 01 03`.

Successful run:

- output directory on phone:
  `/storage/emulated/0/Android/data/com.companyname.bodycam/files/heycyan-probe/20260531-223525`;
- pulled artifacts:
  `captures/phase-4f-20260531-bodycam-csharp-fresh-media-success/20260531-223525/`;
- P2P peer: `M01 Pro_D879B87FE6C9/60:c2:2a:1a:b6:1b`;
- phone group owner IP: `192.168.49.1`;
- glasses media IP: `192.168.49.183`;
- media listing count: `6`;
- JPEG: `20260531223521015.jpg`, `477,137` bytes, `3280x2464`,
  signature `FF D8`;
- MP4: `20260531223525915.mp4`, `6,171,209` bytes, signature `ftypisom`.

This proves the Android C# path can retrieve both images and video from the
glasses without using the official HeyCyan app at runtime.
