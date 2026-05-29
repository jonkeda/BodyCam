# Phase 29 - Fake DAS Local Relay Oracle

**Status:** Attempted - blocked by native DAS validation / no local relay open

## Goal

Make the Android native Vue990/PPCS stack connect to a relay listener we control
so we can capture the real session-open byte sequence without VPN packet
capture, phone rooting, or video bridging.

The purpose is not to relay or stream video. The purpose is to learn the exact
native handshake bytes needed for a pure C# Windows/Android implementation.

## Why This Phase Exists

Phase 28 proved two important facts:

- tiny native packet creators match our managed C# builders;
- native `TCPSend_Hello` emits a 12-byte payload; the first observed value was:
  `000468007351673D7C5897F9`.

Phase 25 also proved that sending those known bytes to real decoded relay hosts
is not enough to get response bytes. The next unknown is the larger second-stage
payload, likely `TCPRlyReq` / `TCPRSLgn`.

The cleanest way to recover it is to let the vendor native library produce it
while we listen locally.

## Preparation Completed

- Added managed DAS re-encoding:
  `A9Vue990DasServerParameter.EncodeDecodedPayload(...)`.
- Added a round-trip unit test proving the current live decoded DAS payload
  re-encodes to the original `DAS-...` value.
- Added `BodyCam.A9Probe vue990-das --replace-relays ... --server-only`.
- Added Android probe extras:
  - `server_override`
  - `fake_relay`
- Added a phone-local TCP listener on `0.0.0.0:65527` to record native relay
  connection bytes.

## Attempted Runs

- Short loopback relay token:
  `127.0.0.1-127.0.0.1-127.0.0.1`
- Same-DAS-length loopback relay token:
  `127.000.000.1-127.000.0.1-127.000.000.1`
- Phone Wi-Fi relay token:
  `192.168.168.101-192.168.168.101-192.168.168.101`

Artifacts:

- `.my/plan/m38-a9-camera/captures/phase-29-fake-relay-loopback-2026-05-29-135139/`
- `.my/plan/m38-a9-camera/captures/phase-29-fake-relay-same-length-loopback-2026-05-29-135220/`
- `.my/plan/m38-a9-camera/captures/phase-29-fake-relay-phone-ip-2026-05-29-135251/`

Observed result:

- the Android probe applied each rewritten `DAS-...` override;
- the fake relay listener started successfully on `0.0.0.0:65527`;
- `JNIApi.Connect(...)` returned `4`;
- `JNIApi.Login(...)` returned `False`;
- the fake relay recorded `connections=0` every time.

Conclusion: rewriting relay hosts is not enough. The native stack likely
validates another field in the decoded DAS payload, probably the trailing token
`9047F8F88` or a checksum/signature covering the relay-host token. Phase 29 is
therefore not the fastest route unless that checksum is recovered.

## Work Plan

1. [x] Add an Android C# fake-relay mode that opens a TCP listener on the phone,
   ideally on `127.0.0.1:65527` first and then `0.0.0.0:65527` if needed.
2. [x] Add an Android probe option to override the `serverParam` passed to
   `JNIApi.Connect(...)`.
3. [x] Build a fake `DAS-...` value by replacing the decoded relay-host token with
   local listener hosts, then encrypting it with the managed DAS encoder.
4. [x] Run the native connect path against the fake `DAS-...`.
5. [blocked] Record every byte the native client sends to the fake relay, with timing and
   packet boundaries.
6. [not reached] Feed bounded canned responses only if needed and only after the first native
   request bytes are saved.
7. [x] Save artifacts under `.my/plan/m38-a9-camera/captures/phase-29-*`.

## Candidate Fake Relay Hosts

Try in this order:

- `127.0.0.1-127.0.0.1-127.0.0.1`
- phone Wi-Fi IP repeated, for example
  `192.168.168.101-192.168.168.101-192.168.168.101`

If the native parser rejects changed plaintext length, try preserving block
length by using shorter host tokens padded with trailing zeroes via the DAS
encoder.

## Expected Success Signal

The fake relay accepts a connection from the native PPCS stack and records a
byte sequence larger than the known 12-byte `TCPSend_Hello`. That sequence
should contain the missing `TCPRlyReq` / `TCPRSLgn` material or the next packet
that leads to it.

## Stop Conditions

Stop and document if:

- native `ConnectByServer` refuses fake DAS values before opening a socket;
- the native library ignores local/loopback relay host tokens;
- the app crashes in native code when using fake DAS;
- only the known 12-byte hello is ever emitted and no second-stage bytes are
  produced without a correct relay response.

## How The User Can Help

- Keep the Samsung phone connected over USB with debugging authorized.
- Keep the phone connected to `@MC-0025644` Wi-Fi unless asked to switch.
- Keep the camera powered by USB so it does not disappear mid-run.
- If Android shows a crash dialog, tell Codex immediately and leave the phone
  connected so `adb logcat` can capture the fault.
- If asked, unlock the phone screen so Android will allow the probe app to run
  in the foreground.
