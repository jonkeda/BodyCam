# Phase 30 - Native Second-Stage Packet Oracle

**Status:** Successful oracle capture - relay still silent

## Goal

Recover exact native bytes for the larger relay/session-open packets without
guessing structure fields.

Target native helpers:

- `cs2p2p_PPPP_Proto_TCPSend_TCPRlyReq`
- `cs2p2p_PPPP_Proto_TCPSend_TCPRlyReqDSK`
- `cs2p2p_PPPP_Proto_TCPSend_TCPRSLgn`

## Why This Phase Exists

Phase 28 proved the loopback socket oracle pattern works for
`TCPSend_Hello`: pass a phone-local socket fd into the native function and read
the emitted bytes from a phone-local listener.

The second-stage functions are riskier because their signatures include
endpoint structs, ids, flags, and likely key/checksum fields. This phase maps
those arguments carefully rather than calling them blind.

## Work Plan

1. [x] Parse dynamic symbol names and demangled signatures into a local evidence
   table.
2. [x] Map C++ argument types to C# P/Invoke shapes.
3. [x] Define managed versions of likely native structs, starting with
   `sockaddr_cs2`.
4. [x] Use harmless loopback sockets only; do not send malformed second-stage bytes
   to public relays until the local oracle is stable.
5. [x] Call one native function at a time with minimal known-safe arguments.
6. [x] Record return code, bytes emitted to the local listener, and crash/no-crash
   outcome.
7. [x] Promote stable emitted byte sequences into managed C# packet builders and
   unit tests.

## Live Outcomes

Artifacts:

- `.my/plan/m38-a9-camera/captures/phase-30-native-write-oracle-2026-05-29-135622/`
- `.my/plan/m38-a9-camera/captures/phase-30-native-tcpsend-oracle-2026-05-29-135826/`
- `.my/plan/m38-a9-camera/captures/phase-30-native-tcpsend-oracle-repeat-2026-05-29-135910/`
- `.my/plan/m38-a9-camera/captures/phase-30-relay-native-second-stage-2026-05-29-140000.json`

Recovered local native outputs:

- `Write_TCPRlyReq` wrote 28 bytes:
  `424B47443030300000000011424B3030323536000002F7FF0100007F`
- `Write_TCPRSLgn` wrote 40 bytes:
  `424B47443030300000000011424B3030323536000000000000000000000000000002F7FF0100007F`
- `TCPSend_Hello` wrote 12 bytes in the current run:
  `0004680067C6FE158F32C284`
- `TCPSend_TCPRlyReq` wrote 64 framed bytes:
  `00386800FF4A81098AFBD7FC10B9AC58F88BFB0E502C933ACCA49F4373E35E905B7F5F9FAE89E783C6D3C825A82544AD780282477960DD03ED1BADDC35D6B2B3`
- `TCPSend_TCPRSLgn` wrote 68 framed bytes:
  `003C6800EC294C34003E3364A0270856599C9319361CA5159328E0CE2F1BC4DBE223080EA7179378EAFEFE31E23F984E12B291B1BE4BA15DCB7C604C98DEA2405298FE7D`

The second-stage `TCPSend_*` values were stable across an immediate repeat
with the same arguments. They have been added to the managed
`A9Vue990P2pPacketBuilder` and to the default Windows relay probe candidates.

Relay result:

- TCP `65527` still opened on all decoded relay hosts.
- Native-generated hello, `TCPRlyReq`, `TCPRSLgn`, and hello+second-stage
  sequences produced no response bytes.

Interpretation:

- We have enough evidence to stop broad packet guessing.
- The remaining missing piece is likely the correct dynamic arguments for the
  native helper calls: real relay name/token, key bytes, flags, session ids, or
  endpoint fields from the decoded DAS/session state.
- A pure C# port can now represent the recovered packet shape, but it cannot
  complete a relay session yet.

## Initial Risk Notes

- `TCPSend_TCPRlyReq` likely wants client id, device id/VUID, mode flags,
  relay token, and endpoint material.
- `TCPSend_TCPRSLgn` may represent relay-server login and may need fields from
  the decoded DAS plaintext.
- Wrong struct alignment could crash the Android process. Every attempt must be
  narrow, logged, and easy to rerun after reinstall.

## Expected Success Signal

A local oracle run emits a deterministic packet that is larger than the
12-byte `TCPSend_Hello` and includes the expected ids/tokens or encrypted
variants. The packet can then be sent by the Windows C# relay probe as a named
candidate.

## Stop Conditions

Stop and document if:

- function signatures cannot be called safely from .NET Android;
- every plausible argument layout crashes before emitting bytes;
- the function emits bytes only after hidden native session state is initialized
  by a real `JNIApi.Connect(...)`.

## Next Step After Success

Continue with Phase 31 by replacing fixed oracle payloads with parameterized C#
builders, then recover the missing dynamic fields from either native call sites
or controlled `Read_*`/`Write_*` round trips.
