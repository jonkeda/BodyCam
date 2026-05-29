# Phase 17 - C# Vendor Adapter And Java Reduction

**Status:** In Progress

## Goal

Move the working Vue990/VStarcam camera path out of Java orchestration and into
C#/.NET Android, while still allowing the vendor JNI libraries to do the native
PPCS/player work.

This is an intermediate phase. It is not the final "C# code for everything"
target, because `libOKSMARTPPCS.so` and `libOKSMARTPLAY.so` would still be in
use. It prepares the codebase for that final target by making C# own the state
machine, capture policy, artifacts, and tests.

## Why This Phase Exists

Phase 15 proved the stream path:

- `JNIApi.create`
- `clientSetVuid`
- `connect(connectType=0x3F, p2pType=1)`
- `login(admin, 888888)`
- `writeCgi("livestream.cgi?streamid=10&substream=0&", channel=1)`
- `AppPlayerApi.setPlayerSource(...)`
- `AppPlayerApi.start(...)`

The proof currently lives mostly in `PpcsProbeBridge.java`. That was the fastest
safe route because Vue990's native libraries are JNI libraries that expect Java
class names and Java callback interfaces.

For maintainability, the next step is to move orchestration to C# and leave only
the smallest possible Java or binding surface.

## Current Java Surface

Files currently under `tools/BodyCam.A9PhoneProbe/Java/`:

- `com.vstarcam.JNIApi`
  - Native declarations for the PPCS library.
  - Class name is probably required by JNI export names in `libOKSMARTPPCS.so`.
- `com.vstarcam.app_p2p_api.ClientStateListener`
  - Callback interface for connection state.
- `com.vstarcam.app_p2p_api.ClientCommandListener`
  - Callback interface for command/control payloads.
- `com.vstarcam.app_p2p_api.ClientReleaseListener`
  - Callback interface for native retain/release lifecycle.
- `com.veepai.AppPlayerApi`
  - Native declarations for the VeePai player library.
  - Class name is probably required by JNI export names in `libOKSMARTPLAY.so`.
- `com.bodycam.a9phoneprobe.PpcsProbeBridge`
  - Current orchestration bridge.
  - This is the main file to replace with C#.

## Target Architecture

### C# Owned

- Status fetch and identity parsing.
- PPCS session state machine.
- Retain/release scheduling policy.
- Live-open command.
- Player setup/start/stop sequencing.
- Capture mode gates.
- Report/log artifact writing.
- RealTests and assertions.

### Minimal Java Or Binding Surface

Preferred target:

- Keep only Java classes whose exact package/class names are required by the
  vendor JNI exports.
- Move `PpcsProbeBridge` orchestration into C#.
- Use .NET Android generated bindings for the Java declaration classes where
  they are reliable. Expected generated types include `Com.Vstarcam.JNIApi`,
  `Com.Veepai.AppPlayerApi`, and listener/progress interfaces.
- Implement the callback objects in C# by deriving from `Java.Lang.Object` and
  implementing the generated listener/progress interfaces.

Stretch target:

- Replace thin Java declaration classes with .NET Android generated/bound
  classes if `[Register]` / Java.Interop can satisfy the exact class and method
  names.

Do not force the stretch target if it makes the capture path unstable. The final
pure-C# target belongs to Phase 18.

## Implementation Plan

1. Create a C# `Vue990PpcsSession` service in the Android probe tool.
2. Wrap `com.vstarcam.JNIApi` calls from C# via `JNIEnv`/Java.Interop instead
   of calling `PpcsProbeBridge.runProbe`.
3. Implement C# callback objects for state, command, release, and player
   progress events.
4. Keep native calls that must run on the Android main thread scheduled from
   C#.
5. Preserve the known retain/release lifecycle exactly.
6. Preserve metadata-only mode as the default.
7. Add an explicit capture mode that Phase 16 can use.
8. Build and run the existing metadata probe unchanged from the user's point of
   view.
9. Run `A9PhonePpcsPlayerRealTests` against the C# path.
10. Delete or quarantine `PpcsProbeBridge.java` only after the C# path is proven.

## Technical Notes

- Android JNI native methods exported as `Java_com_vstarcam_JNIApi_*` are tied
  to the Java class name. That means `DllImport` alone is unlikely to replace
  the Java declaration surface.
- C# can call Java static methods through `JNIEnv.FindClass`,
  `GetStaticMethodID`, and `CallStatic*Method`.
- C# can implement Java interfaces when the interfaces are present as Java
  classes or binding types.
- `PpcsProbeBridge.java` does not appear to need an exact vendor-recognized
  class name. It is our orchestration bridge, so it is the right file to retire
  first.
- The native libraries call release callbacks asynchronously, so callback
  lifetime and garbage collection must be pinned/held carefully.
- Callback implementations must catch/log exceptions internally; exceptions
  escaping a Java/native callback can terminate the app.
- The app must not delete Java class references in a way that Android/Mono
  treats as invalid global cleanup; Phase 15 already hit this once.
- `AppPlayerApi.init(File)` currently wraps private native cache methods, so
  either keep that tiny Java helper or expose its native calls safely before
  deleting the wrapper method.

## Tests

- Default metadata probe still completes and saves the same report fields.
- `JNIApi.connect=3`.
- `JNIApi.login=true`.
- live `writeCgi` returns `true`.
- `checkBuffer[90]` is non-zero after live-open.
- player callbacks fire.
- `app_player_draw_info width=640 height=480`.
- clean `AppPlayerApi.stop`, `AppPlayerApi.destroy`, `JNIApi.disconnect`, and
  `JNIApi.destroy`.
- no `AndroidRuntime` fatal crash.
- Phase 16 still-image capture works through the C# orchestration path.

## Implementation Checklist

- [x] Create this Phase 17 plan doc.
- [x] Add a C# `Vue990PpcsSession` or similarly named service.
- [x] Verify generated binding names for `JNIApi`, `AppPlayerApi`, and callback
      interfaces after Android build.
- [x] Add C# wrappers for `JNIApi` static methods.
- [x] Add C# wrappers for `AppPlayerApi` static methods, including
      `screenshot` for Phase 16.
- [x] Add C# callback implementations for state/command/release/progress and
      hold strong references for the full native session lifetime.
- [x] Move the Phase 15 run order from `PpcsProbeBridge.java` into C#.
- [ ] Keep Java only for native declarations/interfaces if required.
- [ ] Prove metadata-only mode with the phone on `@MC-0025644`.
- [ ] Prove Phase 16 image capture uses the C# orchestration path.
- [x] Update RealTests to assert the C# path, not the Java bridge path.
- [x] Remove or stop using `PpcsProbeBridge.java`.

## Current Implementation Status

- `MainActivity` now calls `Vue990PpcsSession` for PPCS mode.
- The old `PpcsProbeBridge.java` file still exists as a fallback/reference, but
  the normal PPCS entry point no longer calls it.
- `A9PhonePpcsPlayerRealTests` now covers both the metadata path and an explicit
  still-image capture path through the C# entry point.
- The explicit video path is also C# owned: when vendor `startDown(...)`
  returns `False`, the runner captures a bounded JPEG sequence and writes an
  MJPEG AVI through `MjpegAviWriter`.
- Generated binding names were verified after build:
  `Com.Vstarcam.JNIApi`, `Com.Veepai.AppPlayerApi`, and the callback
  interfaces match the C# implementation.
- Android build and install passed.
- Live image verification succeeded through the C# entry point. The still-image
  RealTest now passes when `A9_E2E=1`, `A9_PHONE_CAPTURE_E2E=1`, and the phone
  is on `@MC-0025644`.
- Live short-video artifact verification succeeded through the C# entry point.
  `A9PhonePpcsPlayer_CapturesShortVideoArtifact` now passes when
  `A9_E2E=1`, `A9_PHONE_VIDEO_E2E=1`, and the phone is on `@MC-0025644`.

## Acceptance Criteria

- The main camera workflow is authored in C#.
- Java code, if any, is limited to vendor JNI declarations and callback
  interfaces.
- The existing metadata proof still works.
- The explicit image download path from Phase 16 works.
- The explicit bounded video artifact path from Phase 16/21 works.
- RealTests cover the C# orchestration path.

## Non-Goals

- Do not remove `libOKSMARTPPCS.so` or `libOKSMARTPLAY.so` in this phase.
- Do not reverse-engineer the full PPCS protocol here.
- Do not broaden capture beyond the explicit still/short-video gates.
