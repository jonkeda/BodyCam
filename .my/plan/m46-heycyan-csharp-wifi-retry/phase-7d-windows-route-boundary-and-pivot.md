# Phase 7d - Windows Route Boundary And Pivot

## Goal

Record the current Windows route boundary after the Phase 7c real-hardware
probe, keep the diagnostic improvements, and define the next practical route.

This phase exists because Windows can now prove the HeyCyan BLE/control path,
fresh image/video capture commands, and WiFi Direct peer discovery, but it still
does not form a routed media path to the glasses on the current PC/adapter.

## Evidence Summary

Hardware:

- glasses: `M01 Pro_E6C9` / `D8:79:B8:7F:E6:C9`;
- Windows adapter: `Intel(R) Wi-Fi 7 BE200 320MHz`;
- driver: `23.140.0.3`, dated `2025-05-02`;
- Windows reports Wireless Display support: graphics driver yes, Wi-Fi driver
  yes.

What works on Windows:

- BLE connects.
- Battery/version/control commands work.
- Fresh photo command is accepted.
- Fresh video start/stop is accepted after Phase 7a changed stop video to
  `02 01 03`.
- Transfer command returns:
  - SSID/name `M01 Pro_D879B87FE6C9`;
  - password length `9`;
  - BLE-reported IP `192.168.31.1`.
- WiFi Direct peer discovery finds:
  - name `M01 Pro_D879B87FE6C9`;
  - id `WiFiDirect#60:C2:2A:1A:B6:1B`.

What still fails:

- Native WinRT WiFi Direct never produces endpoint pairs.
- The no-prepair `FromIdAsync` path with group-owner intent `0` fails with
  `COMException HRESULT=0x8007001F`.
- The pair-first path reaches a Windows `ConfirmOnly` pairing request, accepts
  it, then `PairAsync` returns `Failed` with protection `None`; the following
  `FromIdAsync` fails with `COMException HRESULT=0x80004005`.
- After a failed WiFi Direct connection attempt, the peer does not reappear
  inside the same transfer window.
- The WLAN fallback only reaches `associating`, then `disconnected`.
- WLAN AutoConfig reports the glasses SSID as an infrastructure profile failure:
  `The specific network is not available`, `RSSI: 255`.

Important prior result from archived M36:

- The commercial Wi-Fi Framework spike also discovered the same peer but failed
  to establish a routed connection.
- That makes the current blocker unlikely to be only a bug in our WinRT wrapper.

## Artifacts

Phase 7c real-hardware probe artifacts:

- `captures/phase-7c-windows-route-probe/20260601-103753/`
- `captures/phase-7c-windows-route-probe/20260601-104330/`
- `captures/phase-7c-windows-route-probe/20260601-104727/`
- `captures/phase-7c-windows-route-probe/20260601-105348/`

The most useful final artifact is:

- `captures/phase-7c-windows-route-probe/20260601-105348/windows-route-probe-result.json`

It contains:

- matched WiFi Direct peer name/id;
- pair-first attempt result;
- WiFi Direct rediscovery timeout;
- empty endpoint pair list;
- no transfer candidates;
- no validated media IP.

## Code Changes Kept

These changes are worth keeping even though the current route is blocked:

- Windows production DI routes `IHeyCyanMediaTransfer` through
  `HeyCyanMediaTransfer`.
- Windows video stop uses the Android-proven `02 01 03` command.
- `WindowsWiFiDirectManager` records matched peer, endpoint pairs, and
  discovery events.
- The Windows route probe writes a timestamped JSON artifact even on failure.
- Candidate media IPs are validation-first; Windows no longer returns an
  unvalidated fallback IP after `/files/media.config` probe failure.
- WiFi Direct connection attempts now preserve diagnostic history, pass the BLE
  password to pairing if Windows asks for a PIN, try a pair-first path, and
  rediscover the peer between failed attempts.
- PowerShell WLAN diagnostics use `-EncodedCommand`, avoiding fragile nested
  quoting when collecting AutoConfig events.

## Decision

Native Windows HeyCyan media transfer is blocked on the current
Intel BE200/Windows stack.

The code now reaches the platform boundary cleanly and captures useful evidence.
Do not spend more time on minor WinRT/netsh variants on this adapter unless a
new external condition changes, such as a different WiFi adapter, a driver
change, or a newly discovered BLE command that explicitly enables legacy AP
mode.

Android remains the proven C#-only media route.

## Recommended Next Options

1. Test a second USB WiFi adapter with WiFi Direct/Miracast support.
   Run the same Phase 7c probe and compare peer discovery, pairing result,
   endpoint pairs, and `/files/media.config`.

2. Test on an unmanaged Windows laptop.
   This rules out enterprise endpoint policy, VPN, or adapter policy effects.

3. Build an Android bridge fallback.
   Android already downloads valid JPEG/MP4 artifacts through C# BLE and C#
   WiFi P2P. A bridge could expose those files to Windows over ADB, USB, or a
   small local LAN/USB relay.

4. Keep Windows as BLE/control/audio-only for HeyCyan until a reliable route is
   found.

## Acceptance

- The Windows boundary is documented with exact hardware evidence.
- The real probe and diagnostics remain available for future adapter tests.
- The roadmap marks Phase 7d as complete and Phase 7 as blocked on the current
  adapter.
