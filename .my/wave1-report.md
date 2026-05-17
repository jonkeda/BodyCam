# M33 Phase 3 Wave 1: HeyCyanAudioInputProvider — Implementation Report

## Files Created
- `src/BodyCam/Services/Audio/IBluetoothAudioInputProvider.cs` — Bluetooth-specific extension of IAudioInputProvider with MAC-aware selection
- `src/BodyCam/Services/Audio/BluetoothAudioInputProvider.cs` — Generic Bluetooth audio input provider that enumerates and selects from available BT HFP/HSP devices by MAC
- `src/BodyCam/Services/Glasses/HeyCyan/HeyCyanAudioInputProvider.cs` — HeyCyan glasses mic provider wrapping generic BT provider

## Files Changed
- `src/BodyCam/ServiceExtensions.cs` — Added DI registration for BluetoothAudioInputProvider and HeyCyanAudioInputProvider

## Build Results
- `dotnet build src/BodyCam/BodyCam.csproj -f net10.0-android` — PASS (28.2s, 91 warnings from Porcupine package)
- `dotnet build src/BodyCam.Tests/BodyCam.Tests.csproj` — PASS

## Test Results
- `dotnet test --filter "FullyQualifiedName~AudioInputManager"` — PASS (8 tests, 137ms)

## Verify Checklist
- [ ] IsAvailable false until session Connected AND endpoint exists — LOGIC VERIFIED (implemented in HeyCyanAudioInputProvider.IsAvailable)
- [ ] StartAsync throws when disconnected/no endpoint — LOGIC VERIFIED (implemented with guard clauses)
- [ ] StartAsync calls SelectEndpointByMacAsync before inner StartAsync — CODE VERIFIED (line 59-60 in HeyCyanAudioInputProvider.cs)
- [ ] AudioChunkAvailable re-emits PCM16 24kHz unchanged — CODE VERIFIED (OnChunk forwards directly, line 67-70)
- [ ] Disconnected fires when inner provider raises it — CODE VERIFIED (OnBtDisconnected forwards, line 72-75)
- [ ] On StateChanged → not Connected, capture stops — CODE VERIFIED (OnSessionStateChanged, line 77-83)
- [ ] DisposeAsync unhooks subscriptions — CODE VERIFIED (line 85-91)
- [ ] DI resolves single shared instance — CODE VERIFIED (ServiceExtensions.cs line 35-40)
- [ ] MANUAL: Pair glasses, connect QCSDK, observe ActiveProviderId == "heycyan-glasses" (requires real HeyCyan glasses hardware)

## Notes / Deviations
- Added `IBluetoothAudioInputProvider` interface (as instructed by wave spec step 1)
- Created generic `BluetoothAudioInputProvider` class (M12 Phase 2 assumed this existed but it did not)
- Generic provider uses `AudioInputManager.Providers` to enumerate device-specific providers registered by platform enumerators
- MAC normalization removes colons/dashes and lowercases for comparison
- Codec contract documented inline (SBC minimum, mSBC if OS negotiates, no aptX/LDAC guarantees)

## Next Wave Hint
Wave 2: ../wave2-heycyan-audio-output-provider.md (HeyCyanAudioOutputProvider — symmetric implementation for speaker)
