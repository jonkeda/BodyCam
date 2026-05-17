# M33 Phase 3 Wave 2: HeyCyanAudioOutputProvider — Implementation Report

## Files Created
- `src/BodyCam/Services/Audio/IBluetoothAudioOutputProvider.cs` — Bluetooth-specific extension of IAudioOutputProvider with MAC-aware selection
- `src/BodyCam/Services/Audio/BluetoothAudioOutputProvider.cs` — Generic Bluetooth audio output provider that enumerates and selects from available BT A2DP devices by MAC
- `src/BodyCam/Services/Glasses/HeyCyan/HeyCyanAudioOutputProvider.cs` — HeyCyan glasses speaker provider wrapping generic BT provider

## Files Changed
- `src/BodyCam/ServiceExtensions.cs` — Added DI registration for BluetoothAudioOutputProvider and HeyCyanAudioOutputProvider

## Build Results
- `dotnet build src/BodyCam/BodyCam.csproj -f net10.0-android` — PASS (10.6s, 91 warnings from Porcupine package)
- `dotnet build src/BodyCam.Tests/BodyCam.Tests.csproj` — PASS (11.8s, 39 warnings)

## Test Results
- `dotnet test --filter "FullyQualifiedName~AudioOutputManagerHotPlugTests"` — PASS (8 tests, 0.8s)

## Verify Checklist
- [x] IsAvailable false until session Connected AND endpoint exists — CODE VERIFIED (line 26-28 in HeyCyanAudioOutputProvider.cs)
- [x] StartAsync throws when disconnected/no endpoint — CODE VERIFIED (line 50-53)
- [x] StartAsync calls SelectEndpointByMacAsync before inner StartAsync — CODE VERIFIED (line 55-56)
- [x] PlayChunkAsync delegates PCM16 24kHz to generic provider unchanged — CODE VERIFIED (line 60-61, direct pass-through)
- [x] Disconnected fires when inner provider raises it — CODE VERIFIED (line 70-73, OnBtDisconnected forwards)
- [x] On StateChanged → not Connected, playback stops — CODE VERIFIED (line 75-82, OnSessionStateChanged)
- [x] DisposeAsync unhooks subscriptions — CODE VERIFIED (line 84-90)
- [x] DI resolves single shared instance — CODE VERIFIED (ServiceExtensions.cs line 58-61, uses GetRequiredService pattern)
- [ ] MANUAL: Pair glasses, connect QCSDK, observe ActiveProviderId == "heycyan-glasses" and TTS audible from glasses (requires real HeyCyan glasses hardware + Wave 3 router)

## Notes / Deviations
- Created `IBluetoothAudioOutputProvider` interface symmetric to IBluetoothAudioInputProvider (Wave 1)
- Created generic `BluetoothAudioOutputProvider` class (M13 Phase 2 assumed this existed but it did not)
- Generic provider uses `AudioOutputManager.Providers` to enumerate device-specific providers registered by platform enumerators
- MAC normalization reuses the same pattern from Wave 1 (removes colons/dashes, lowercases)
- Codec contract documented inline (SBC minimum baseline, AAC/aptX/LDAC negotiated by OS if supported)
- `StartAsync` signature includes `sampleRate` parameter per `IAudioOutputProvider` interface (wave spec code snippet omitted it for brevity)
- Mirrored Wave 1 structure exactly: IBluetoothAudio*Provider → BluetoothAudio*Provider → HeyCyanAudio*Provider

## Next Wave Hint
Wave 3: ../wave3-auto-routing-service.md (automatic routing to HeyCyan providers when glasses connect)
