# M43 Phase 3 - Platform Route Validation

Goal: validate provider capabilities and echo policy on Windows, Android, and
iOS with real route changes.

## Why

Unit tests can prove policy logic, but platform audio stacks decide what route
is actually active. This phase checks that each platform reports enough state
to assign correct provider capabilities and avoid unnecessary AEC.

## Scope

- Verify provider capabilities on Windows, Android, and iOS.
- Verify route monitor updates on real route changes.
- Confirm platform-native AEC behavior where used.
- Confirm no assistant self-reply loop on direct speakers.
- Confirm headset routes do not run unnecessary AEC.
- Record route policy logs for each scenario.

## Windows

Files to inspect/update:

- `src/BodyCam/Platforms/Windows/PlatformMicProvider.cs`
- `src/BodyCam/Platforms/Windows/WindowsSpeakerProvider.cs`
- `src/BodyCam/Platforms/Windows/WindowsRouteMonitor.cs`
- `src/BodyCam/Platforms/Windows/Audio/WindowsBluetoothAudioProvider.cs`
- `src/BodyCam/Platforms/Windows/Audio/WindowsBluetoothAudioOutputProvider.cs`
- `src/BodyCam/Platforms/Windows/Audio/WindowsBluetoothEnumerator.cs`
- `src/BodyCam/Platforms/Windows/Audio/WindowsBluetoothOutputEnumerator.cs`

Validation matrix:

| Route | Expected capability | Expected AEC |
| --- | --- | --- |
| Laptop speaker | direct device speaker, needs AEC | WebRTC APM |
| Wired headphones | isolated headset | off |
| Bluetooth headset | isolated headset | off |
| Bluetooth room speaker | external room speaker, needs AEC | WebRTC APM |
| HeyCyan glasses audio | glasses/wearable, isolated by default | off |

Windows route classification should prefer endpoint form factor and device role
when available. Friendly-name heuristics can remain as fallback only.

## Android

Files to inspect/update:

- `src/BodyCam/Platforms/Android/PlatformMicProvider.cs`
- `src/BodyCam/Platforms/Android/PhoneSpeakerProvider.cs`
- `src/BodyCam/Platforms/Android/AndroidRouteMonitor.cs`
- `src/BodyCam/Platforms/Android/Audio/AndroidBluetoothAudioProvider.cs`
- `src/BodyCam/Platforms/Android/Audio/AndroidBluetoothAudioOutputProvider.cs`

Validation matrix:

| Route | Expected capability | Expected AEC |
| --- | --- | --- |
| Phone speaker | direct device speaker, needs AEC | platform native |
| Wired headset | isolated headset | off |
| USB headset | isolated headset | off |
| Bluetooth SCO headset | isolated headset | off |
| Bluetooth A2DP room speaker | external room speaker, needs AEC | platform native or WebRTC fallback |
| HeyCyan glasses audio | glasses/wearable, isolated by default | off |

Confirm that `AudioRecord` and `AudioTrack` session sharing works on at least
one real Android device. Do not assume emulator audio proves acoustic behavior.

## iOS

Files to inspect/update:

- `src/BodyCam/Platforms/iOS/PlatformMicProvider.cs`
- `src/BodyCam/Platforms/iOS/PhoneSpeakerProvider.cs`
- `src/BodyCam/Platforms/iOS/IosRouteMonitor.cs`

Validation matrix:

| Route | Expected capability | Expected AEC |
| --- | --- | --- |
| iPhone speaker | direct device speaker, needs AEC | VoiceProcessingIO |
| AirPods | isolated headset | off |
| Bluetooth HFP/A2DP | isolated headset or external speaker, depending route | capability-driven |
| Wired/USB-C headset | isolated headset | off |

Confirm that VoiceProcessingIO is active only when the route needs echo
cancellation. Avoid stacking WebRTC APM over VoiceProcessingIO by default.

## Test Evidence

For each route, capture:

- platform
- input provider ID and capabilities
- output provider ID and capabilities
- route monitor state
- selected `AecMode`
- selected `VoiceCleanupMode`
- estimated latency
- policy explanation
- whether assistant self-reply was observed

## Acceptance

- Windows direct speaker does not loop assistant audio back into the session.
- Android phone speaker uses platform-native AEC by default.
- iOS phone speaker uses VoiceProcessingIO by default.
- Headsets and glasses bypass AEC on all platforms.
- Bluetooth headset and Bluetooth room speaker can be distinguished when the
  platform exposes enough information.
- Every validated route has a recorded policy log.
