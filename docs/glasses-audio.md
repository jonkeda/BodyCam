# HeyCyan Glasses Audio

This document describes the audio configuration and codec support for HeyCyan glasses.

## Audio Architecture

HeyCyan glasses expose themselves as a standard Bluetooth headset with two audio paths:

1. **Live conversation audio** (Realtime API):
   - **Input**: HFP/SCO (Hands-Free Profile with Synchronous Connection-Oriented audio)
   - **Output**: A2DP (Advanced Audio Distribution Profile)
   - Used for real-time voice conversations with the Realtime API
   - Automatically routed when glasses connect (see `HeyCyanAudioRouter`)

2. **Recorded media** (post-hoc):
   - Audio recordings stored on the glasses
   - Retrieved via WiFi transfer as OPUS files
   - Not part of the live audio pipeline

## Codec Policy

### Guaranteed Support

HeyCyan glasses are guaranteed to work over:
- **A2DP**: SBC (Sub-Band Coding)
- **HFP**: CVSD (Continuous Variable Slope Delta modulation)

These codecs represent the baseline that all Bluetooth headsets must support.

### Optional Codecs

The following higher-quality codecs **may** be negotiated by the OS Bluetooth stack but are **not promised**:
- **A2DP**: AAC, aptX, aptX-HD, LDAC
- **HFP**: mSBC (modified SBC, "wideband speech")

Whether these codecs are used depends on:
- The phone's Bluetooth stack capabilities
- The glasses' firmware codec support
- Signal conditions and link quality
- OS-specific Bluetooth preferences

## Codec Diagnostics

### Android

On Android API 28+ (Oreo MR1), the app queries the negotiated codec via `BluetoothA2dp` and `BluetoothHeadset` profile proxies. The diagnostics service (`IHeyCyanAudioDiagnostics`) reports:

- **A2DP codec**: SBC / AAC / aptX / aptX-HD / LDAC / unknown
- **Sample rate**: 16000 / 22050 / 24000 / 32000 / 44100 / 48000 / 88200 / 96000 Hz
- **Channels**: mono (1) / stereo (2)
- **HFP codec**: CVSD / mSBC / unknown

> **Note**: Due to limitations in the Android SDK bindings, A2DP codec detection may fall back to reporting "SBC" as the assumed baseline. This does not affect audio quality—the actual negotiated codec is used by the OS.

### iOS

iOS deliberately **does not expose** Bluetooth codec details to third-party apps. The diagnostics service returns all codec fields as `null` or `0` on iOS. This is a platform limitation, not a bug.

## Usage in Settings UI

The diagnostics service can be injected into a settings page to display the current audio configuration:

```csharp
@inject IHeyCyanAudioDiagnostics Diagnostics

<Label Text="@CodecSummary" />

@code {
    private string CodecSummary =>
        Diagnostics.Current is { } info
            ? $"A2DP: {info.NegotiatedA2dpCodec ?? "unknown"} · {info.SampleRateHz} Hz · {info.Channels}ch | HFP: {info.HfpCodec ?? "unknown"}"
            : "Disconnected";

    protected override void OnInitialized()
    {
        Diagnostics.Updated += (_, _) => InvokeAsync(StateHasChanged);
    }
}
```

## Performance Expectations

- **SBC** (baseline): acceptable quality for voice conversations; limited music fidelity.
- **AAC**: improved music quality; wider adoption on modern devices.
- **aptX / aptX-HD**: lower latency, higher bitrate; primarily on Android devices.
- **LDAC**: highest quality A2DP codec; primarily Sony devices.
- **CVSD** (HFP baseline): 64 kbps narrowband speech (8 kHz).
- **mSBC** (HFP wideband): 16 kbps wideband speech (16 kHz); clearer voice capture.

## Troubleshooting

### No audio from glasses

1. Verify Bluetooth pairing in system settings
2. Check that `HeyCyanAudioRouter` has routed to "heycyan-glasses" providers
3. Confirm `IHeyCyanGlassesSession.State == Connected`
4. On Android, ensure `BLUETOOTH_CONNECT` permission granted

### Codec shows "unknown" or "SBC" on high-end device

This is expected. The OS negotiates codecs dynamically, and the app only observes the result. If the binding cannot query the codec status, it reports "SBC" as the conservative fallback. The actual codec in use may be higher quality.

### iOS reports all codecs as "unknown"

This is expected. iOS does not provide third-party apps access to codec information. Audio quality is still governed by what iOS negotiates with the glasses.

## Related Services

- `IHeyCyanGlassesSession` — BLE control session (pairing, commands, battery)
- `HeyCyanAudioInputProvider` — wraps generic BT input, locks to glasses MAC
- `HeyCyanAudioOutputProvider` — wraps generic BT output, locks to glasses MAC
- `HeyCyanAudioRouter` — automatically switches providers on connect/disconnect
- `IHeyCyanAudioDiagnostics` — reports negotiated codecs (this document)
- `BluetoothAudioInputProvider` — generic BT HFP capture provider
- `BluetoothAudioOutputProvider` — generic BT A2DP playback provider
