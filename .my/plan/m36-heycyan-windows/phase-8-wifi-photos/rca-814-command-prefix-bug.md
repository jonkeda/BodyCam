# RCA-814: Missing 0x02 Prefix on GlassesControl Commands

## Root Cause

All `glassesControl` BLE commands require a `0x02` prefix byte (command-type/write-direction indicator).
Only `EnterTransferMode` had it correct (`[0x02, 0x01, 0x04]`). All other commands were missing it:

| Command | Wrong | Correct |
|---------|-------|---------|
| StartPhotoMode | `[0x01, 0x01]` | `[0x02, 0x01, 0x01]` |
| TakeAiPhoto | `[0x01, 0x06, 0x02, 0x02]` | `[0x02, 0x01, 0x06, 0x02, 0x02]` |
| StopMode | `[0x01, 0x0b]` | `[0x02, 0x01, 0x0b]` |
| ExitTransferMode | `[0x01, 0x09]` | `[0x02, 0x01, 0x09]` |
| ResetP2p | `[0x01, 0x0F]` | `[0x02, 0x01, 0x0F]` |
| StartVideoRecording | `[0x01, 0x02]` | `[0x02, 0x01, 0x02]` |
| StartAudioRecording | `[0x01, 0x08]` | `[0x02, 0x01, 0x08]` |
| GetMediaCounts | `[0x04]` | `[0x02, 0x04]` |

## Evidence

Android SDK (`CyanBridge/MainActivity.kt`) — EVERY `glassesControl` call uses `0x02` as first byte:
```kotlin
// Photo capture (line ~674)
LargeDataHandler.getInstance().glassesControl(byteArrayOf(0x02, 0x01, 0x01)) { ... }

// Enter transfer (line ~2880)
LargeDataHandler.getInstance().glassesControl(byteArrayOf(0x02, 0x01, 0x04)) { ... }

// Exit transfer
LargeDataHandler.getInstance().glassesControl(byteArrayOf(0x02, 0x01, 0x09)) { ... }
```

## Why EnterTransferMode Worked

The `EnterTransferMode` command happened to be copied correctly from the Android SDK early on.
Other commands were reverse-engineered from partial documentation without the `0x02` prefix.

## Impact

- Photo capture command was silently ignored by glasses (glasses responded with ACK but no action)
- ExitTransferMode may not have been fully exiting (explaining re-entry issues)
- The `workTypeIng` response from glasses indicated state but the commands weren't actually changing it

## Additional Bug: Cannot Capture While In Transfer Mode

The glasses' state machine rejects photo/video commands while WiFi transfer is active.
The test was calling `TakePhotoAsync` WHILE in transfer mode — even with correct bytes this would fail.

Correct sequence:
1. Exit transfer mode → `[0x02, 0x01, 0x09]`
2. Wait 2s for WiFi to shut down
3. Take photo → `[0x02, 0x01, 0x01]` (atomic: enters camera mode + captures)
4. Wait 5s for photo to be written to flash
5. Re-enter transfer mode → `[0x02, 0x01, 0x04]`
6. Connect WiFi, list files, download

## Fix

- `HeyCyanCommands.cs`: Added `0x02` prefix to all 8 affected commands
- `CaptureAndDownloadTests.cs`: Reordered to exit transfer before capture
- `WindowsHeyCyanGlassesSession.cs`: Simplified transfer flow (sequential, no parallel scanning)
