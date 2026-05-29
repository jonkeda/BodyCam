# Phase 28 - Android Native Packet Oracle

**Status:** Completed for tiny packet creators and TCP hello loopback

## Goal

Use the Android C# probe as a safe native packet oracle for exported
`libOKSMARTPPCS.so` packet creator functions, without packet capture and without
bridging camera video.

## Implementation

- Added `Vue990NativePacketOracle` to the Android C# probe.
- Added an autorun intent mode: `--ez native_oracle true`.
- The oracle loads the Vue990 PPCS native library path as needed by P/Invoke,
  allocates a 256-byte buffer, and calls only the tiny creator functions first:
  `create_Hello`, `create_RlyHello`, and `create_SvrReq`.
- Added optional `--ez native_oracle_socket true`, which creates a local
  loopback TCP socket on the phone, passes the connected socket fd to native
  `TCPSend_Hello`, and reads the emitted bytes from the local listener.

## Run

Command:

```powershell
adb shell am start -n com.bodycam.a9phoneprobe/crc64ae57c528e26a7b15.MainActivity --ez autorun true --ez native_oracle true
```

Artifact:

- `.my/plan/m38-a9-camera/captures/phase-28-native-packet-oracle/a9-native-packet-oracle-2026-05-29-132028.txt`
- `.my/plan/m38-a9-camera/captures/phase-28-native-packet-oracle/a9-native-packet-oracle-socket-2026-05-29-132343.txt`

## Result

- `create_Hello`: return `4`, bytes `F1000000`
- `create_RlyHello`: return `4`, bytes `F1700000`
- `create_SvrReq`: return `4`, bytes `F2100000`
- `TCPSend_Hello` loopback: return `0`, first observed `12` bytes:
  `000468007351673D7C5897F9`

These are now represented in `A9Vue990P2pPacketBuilder`.

Phase 30 later observed another valid `TCPSend_Hello` value,
`0004680067C6FE158F32C284`, while the 4-byte `00046800` prefix remained
stable. Sending the observed 12-byte `TCPSend_Hello` payloads to the decoded
TCP `65527` relay hosts produced no response bytes:

- `.my/plan/m38-a9-camera/captures/phase-25-relay-native-tcpsend-hello-2026-05-29-132343.json`

## Conclusion

The first managed Windows relay headers and native TCP-send hello are correct
enough to reproduce native output, but still not enough for a relay response on
their own. The next unknown is the larger second-stage session payload, likely
`TCPRlyReq` / `TCPRSLgn`, which carries ids, endpoint material, flags, and
possibly crypto/checksum fields.

## Next Step

Use the same loopback pattern for `TCPSend_TCPRlyReq` after its endpoint-struct
arguments are mapped.
