# Phase 32 - Parameterized Second-Stage Fields

**Status:** Started - native write-struct offsets partially mapped

## Goal

Replace fixed Phase 30 oracle packet constants with real C# builders for
`TCPRlyReq` and `TCPRSLgn` that compute the correct dynamic fields for the live
camera/session.

## Why This Phase Exists

Phase 30 recovered the packet shapes:

- `TCPSend_TCPRlyReq` emits a 64-byte frame.
- `TCPSend_TCPRSLgn` emits a 68-byte frame.

Sending those fixed frames to the decoded relay hosts opened TCP sockets but
returned no bytes. That means the relay probably checks dynamic fields such as
device id, relay token, key bytes, endpoint material, flags, counters, or
session nonce values.

## Evidence To Use

- Decoded DAS tokens:
  - `53BAH050-\x13\x11r/=K=00011`
  - `a+a+a`
  - `47.98.128.117-120.78.3.33-47.109.80.221`
  - `BKGD`
  - `9047F8F88`
- Camera identity:
  - client id: `BKGD00000100FMQLN`
  - VUID: `BK0025644WBPD`
- Phase 30 native write outputs:
  - `Write_TCPRlyReq`
  - `Write_TCPRSLgn`
- Phase 30 native send outputs:
  - `TCPSend_TCPRlyReq`
  - `TCPSend_TCPRSLgn`

## Work Plan

1. [x] Add Android oracle variants that change one native argument at a time:
   client id, VUID, relay token, key bytes, flags, `sockaddr_cs2`, and integer
   fields.
2. [partial] Diff emitted `Write_*` structs and `TCPSend_*` frames to map each argument
   to byte offsets.
3. [ ] Add managed C# packet builder tests for each identified field offset.
4. [ ] Try `Read_TCPRlyReq` / `Read_TCPRSLgn` round trips against native-written
   buffers to confirm struct layout.
5. [ ] Use live DAS tokens as arguments instead of placeholder `BKGD` / zero key
   values.
6. [ ] Send the parameterized C# frames to decoded relay TCP `65527` hosts.
7. [ ] If any relay returns bytes, save the response and move back into Phase 31
   login/control-channel implementation.

## First Variant Run

Artifact:

- `.my/plan/m38-a9-camera/captures/phase-32-write-variant-oracle-2026-05-29-141000/`

Observed `Write_TCPRlyReq` layout movement:

- client id affects bytes `0..6`; byte `11` stores the client-id length.
- VUID affects bytes `12..18`.
- changing the relay-token argument did not change the write struct in this
  call shape.
- `sockaddr_cs2` affects the final 8 bytes in the baseline 28-byte struct.
- `mode`, `sessionKey`, and `flag` are written beyond the baseline non-zero
  tail when non-zero values are provided.

Observed `Write_TCPRSLgn` layout movement:

- client id affects bytes `0..6`; byte `11` stores the client-id length.
- VUID affects bytes `12..18`.
- the four `ushort` fields and one `uint` field occupy the middle zero region
  before the final `sockaddr_cs2`.
- `sockaddr_cs2` again affects the final 8 bytes.

Immediate inference: the native write structs are fixed-width/truncated for
ids, and the relay-token argument is probably not part of `Write_TCPRlyReq` in
the way we guessed. The next useful oracle should vary the `TCPSend_*` wrappers,
not only `Write_*`, and should test live DAS token `9047F8F88` in the argument
that actually influences the encrypted/framed output.

## Expected Success Signal

At least one relay returns response bytes after a C#-built `TCPRlyReq` or
`TCPRSLgn` frame using live camera/session arguments.

## Stop Conditions

Stop and document if:

- the native helper output does not change when expected arguments change;
- required fields only appear after hidden native session initialization;
- relays require a server-side state that cannot be created from standalone TCP
  frames.

## How The User Can Help

- Keep the phone USB-connected with debugging authorized.
- Keep the phone and Windows on `@MC-0025644` when live relay probes are running.
- Keep the camera powered by USB.
- Tell Codex if the Vue990 app still shows live video while probes are failing;
  that helps separate protocol mistakes from camera/network state.
