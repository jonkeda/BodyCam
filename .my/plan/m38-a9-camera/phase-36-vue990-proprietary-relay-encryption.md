# Phase 36 - Vue990 Proprietary Relay Encryption

**Status:** Started - managed C# codec ported, relay packet builder still open

## Goal

Move the Windows/Android C# implementation past fixed native-oracle relay
frames by porting the proprietary encryption layer used by the Vue990/OKSMART
relay helpers.

## Why This Phase Exists

Phase 35 ruled out a local Android C# stream path for this camera. Phase 34
showed that Windows can open TCP sockets to the decoded relay hosts, but simple
fixed replay frames do not produce response bytes. The likely missing piece is
the encrypted second-stage relay request, not JPEG/video decoding.

## Evidence

Native exports in `libOKSMARTPPCS.so` include:

- `_Z31cs2p2p__P2P_Proprietary_EncryptPKcPKhPht`
- `_Z31cs2p2p__P2P_Proprietary_DecryptPKcPKhPht`
- `_Z29_TCPRelay_Proprietary_EncryptPKhS0_Pht`
- `_Z29_TCPRelay_Proprietary_DecryptPKhS0_Pht`
- `cs2p2p_gCRCKey`
- `cs2p2p_gP2PKeyString`

The native disassembly shows that the TCP relay wrapper builds a key string
from the first two seed bytes as `%02X%02X`, then calls the same P2P
proprietary codec. The P2P codec uses the 256-byte table already present in the
known PPPP XOR discovery path.

Public reference used for the generic PPPP framing:

- <https://github.com/DavidVentura/cam-reverse>

## Implemented

- Extended `A9Vue990PpcsEncryptionCodec` with:
  - fixed-key XOR compatibility
  - derived proprietary key bytes
  - proprietary encode/decode
  - TCP relay encode/decode wrapper
- Added regression tests proving:
  - fixed-key XOR still matches `F1300000 -> 2CBA5F5D`
  - derived proprietary codec round-trips
  - TCP relay wrapper round-trips

## Live Relay Run

Artifact:

- `.my/plan/m38-a9-camera/captures/phase-34-windows-relay-cached-2026-05-29-1520.json`

Observed:

- Windows had Ethernet internet plus camera Wi-Fi.
- Decoded relays:
  - `47.98.128.117`
  - `120.78.3.33`
  - `47.109.80.221`
- Windows opened relay TCP `65527` for most candidates.
- No candidate produced response bytes.

## Next Work

1. [ ] Build C# `TCPRlyReq` and `TCPRSLgn` plain structs from the Phase 32
   offset mapping.
2. [ ] Identify the correct TCP relay encryption seed for those structs.
3. [ ] Reproduce one native `TCPSend_TCPRlyReq` byte-for-byte in C#.
4. [ ] Send the byte-for-byte C# relay request from Windows.
5. [ ] If response bytes arrive, save raw `.bin`, then parse the channel/session
   envelope.
6. [ ] Send the managed live CGI command over the recovered channel.
7. [ ] Save one image and one short video.
