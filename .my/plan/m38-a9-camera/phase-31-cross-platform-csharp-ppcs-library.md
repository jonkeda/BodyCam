# Phase 31 - Cross-Platform C# PPCS Library

**Status:** Planned - final conversion target, now unblocked at packet-shape level

## Goal

Turn the recovered Vue990/HLP2P/PPCS protocol into a C#-only library that can
run on Windows and Android without Vue990 native libraries.

## Dependencies

- Phase 24: DAS decode.
- Phase 25: relay reachability and managed probe shell.
- Phase 28: native packet oracle for first hello packets.
- Phase 29: fake-DAS local relay attempt, blocked by likely DAS checksum.
- Phase 30: native second-stage packet oracle. We now have C# constants for
  native-generated `TCPSend_TCPRlyReq` and `TCPSend_TCPRSLgn` frame shapes.

## Latest Input From Phase 30

Managed C# now contains:

- `A9Vue990P2pPacketBuilder.BuildTcpSendHelloOracle()`
- `A9Vue990P2pPacketBuilder.BuildTcpSendRlyReqOracle()`
- `A9Vue990P2pPacketBuilder.BuildTcpSendRsLgnOracle()`

The decoded relay sockets still return no bytes when those fixed oracle frames
are sent, which means Phase 31 must parameterize the dynamic fields instead of
only replaying one captured oracle value.

## Library Shape

Suggested core components:

- `A9Vue990DasServerParameter` - decode and encode `DAS-...`.
- `A9Vue990RelayClient` - TCP relay connection, hello, request, keepalive.
- `A9Vue990Session` - connect/login/channel lifecycle.
- `A9Vue990CgiCommandBuilder` - known live-open command framing.
- `A9Vue990StreamReader` - bounded channel `1` byte reader.
- `A9Vue990ImageExtractor` - identify JPEG frames or codec payloads.
- `A9MjpegAviWriter` - package frame sequences when JPEG frames are available.

## Milestones

1. [partial] Reproduce native relay hello and second-stage request in C#.
2. Parameterize dynamic fields in `TCPRlyReq` / `TCPRSLgn`: ids, relay token,
   key bytes, flags, endpoint material, and session counters.
3. Receive first relay response bytes from Windows C#.
4. Complete login/control channel from Windows C#.
5. Send `livestream.cgi?streamid=10&substream=0&` from Windows C#.
6. Save bounded raw channel `1` stream bytes.
7. Decode/extract one still image.
8. Save a short video artifact.
9. Run the same managed library on Android without `libOKSMARTPPCS.so` or
   `libOKSMARTPLAY.so`.

## Cross-Platform Rules

- Core protocol code must stay platform-neutral .NET.
- Android-specific ADB/oracle code remains in tools, not the library.
- Windows-only firewall/topology diagnostics remain in tools.
- Native Vue990 libraries can remain as test oracles until this phase is
  complete, but must not be required by the final runtime path.

## Completion Criteria

- Windows C# retrieves a still image and short video without phone helper or
  vendor native libraries.
- Android C# retrieves the same artifacts without Vue990 native libraries.
- Hardware-gated RealTests cover both direct Windows and Android managed paths.
