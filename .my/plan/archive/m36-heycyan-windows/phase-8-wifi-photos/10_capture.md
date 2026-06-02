# 10_Capture — WiFi Photo Capture Test Plan

**Goal:** Validate the end-to-end WiFi transfer pipeline by capturing a photo on the glasses and downloading it to the PC.

---

## Prerequisites

- HeyCyan M01 Pro glasses powered on, charged, paired via Bluetooth
- Windows PC with Bluetooth + WiFi radios
- Environment variables set:
  ```powershell
  $env:BODYCAM_REAL_HEYCYAN = "1"
  $env:BODYCAM_REAL_HEYCYAN_MAC = "D8:79:B8:7F:E6:C9"
  ```

## Test: `CaptureAndDownloadTests.CapturePhoto_Download_SaveToDisk`

Existing test in `src/BodyCam.RealTests/Services/Glasses/HeyCyan/CaptureAndDownloadTests.cs`.

### Sequence

1. **Connect** — `SharedHeyCyanWiFiFixture` connects BLE + enters transfer mode via `CreateWithTransferAsync`
2. **Exit transfer** — Glasses can't capture while in WiFi transfer mode, so exit first
3. **Take photo** — Send `StartPhotoMode` (0x41 [02 01 01]) twice (first = enter camera mode, second = shutter), plus `TakeAiPhoto` as fallback
4. **Wait 5s** — Firmware needs time to write the JPEG to internal storage
5. **Re-enter transfer mode** — This now exercises the **new WiFi transfer plan** (Steps 2-8):
   - Step 2: `PollWifiIpReadyAsync` — poll `GetWifiIP` (0x41 [02 03]) with exponential backoff
   - Step 3: `GetDeviceConfig` (0x47) — critical AP radio activation signal
   - Step 4: 5s wait for AP radio spin-up
   - Step 5: Confirm AP via second `PollWifiIpReadyAsync`
   - Step 6: `ForceJoinAsync` — tries WPA2PSK/AES, then WPAPSK/TKIP, then open/none
   - Step 7: Second `GetDeviceConfig` to keep glasses in transfer state
   - Step 8: 15s post-connect wait with BLE keepalive polls every 5s
6. **List media** — HTTP GET `http://<glasses-ip>/files/media.config`, parse photo entries
7. **Download newest photo** — HTTP GET the JPEG file
8. **Validate JPEG** — Check SOI (FF D8) and EOI (FF D9) markers, size > 1KB
9. **Save to disk** — Write to `TestResults/rca-811-capture.jpg`
10. **Cleanup** — Exit transfer mode

### Run Command

```powershell
$env:BODYCAM_REAL_HEYCYAN="1"; $env:BODYCAM_REAL_HEYCYAN_MAC="D8:79:B8:7F:E6:C9"
dotnet test src/BodyCam.RealTests -f net10.0-windows10.0.19041.0 `
  --filter "FullyQualifiedName~CaptureAndDownloadTests.CapturePhoto_Download_SaveToDisk" `
  -v normal --logger "console;verbosity=detailed"
```

## Test: `CaptureAndDownloadTests.DownloadExistingPhoto_SaveToDisk`

Lighter variant — skips photo capture, downloads the first existing photo on the glasses.

### Run Command

```powershell
$env:BODYCAM_REAL_HEYCYAN="1"; $env:BODYCAM_REAL_HEYCYAN_MAC="D8:79:B8:7F:E6:C9"
dotnet test src/BodyCam.RealTests -f net10.0-windows10.0.19041.0 `
  --filter "FullyQualifiedName~CaptureAndDownloadTests.DownloadExistingPhoto_SaveToDisk" `
  -v normal --logger "console;verbosity=detailed"
```

## Test: `WindowsWiFiTransferTests.EnterTransferMode_ReceivesIpAddress`

Minimal smoke test — validates BLE transfer mode entry returns a valid IPv4 address. Good first test to confirm the new BLE choreography (GetDeviceConfig + confirmation poll) works before attempting photo download.

### Run Command

```powershell
$env:BODYCAM_REAL_HEYCYAN="1"; $env:BODYCAM_REAL_HEYCYAN_MAC="D8:79:B8:7F:E6:C9"
dotnet test src/BodyCam.RealTests -f net10.0-windows10.0.19041.0 `
  --filter "FullyQualifiedName~WindowsWiFiTransferTests.EnterTransferMode_ReceivesIpAddress" `
  -v normal --logger "console;verbosity=detailed"
```

---

## Execution Order (recommended)

| # | Test | What it validates |
|---|------|-------------------|
| 1 | `EnterTransferMode_ReceivesIpAddress` | BLE choreography (GetDeviceConfig 0x47, IP confirmation) |
| 2 | `DownloadExistingPhoto_SaveToDisk` | WiFi join (WPA2PSK/AES profile) + HTTP download |
| 3 | `CapturePhoto_Download_SaveToDisk` | Full pipeline: capture → transfer → download → save |

## Expected Output

- Console stderr shows step-by-step BLE/WiFi progress (`[BLE] Step 3:`, `[WIFI] Step 6:`, etc.)
- `TestResults/rca-811-capture.jpg` — captured photo saved to disk
- `TestResults/rca-811-existing-photo.jpg` — existing photo downloaded

## Failure Diagnostics

| Symptom | Likely cause | Fix |
|---------|-------------|-----|
| `GetDeviceConfig — no response` | Glasses firmware doesn't respond to 0x47 | Harmless, code continues anyway |
| `AP confirmation failed` | AP didn't start in time | Increase Step 4 wait or retry |
| `Profile WPA2PSK/AES failed, trying WPAPSK/TKIP` | AP uses WPA1 | Expected fallback behavior |
| `No connection with any profile` | AP not broadcasting or adapter issue | Run Step 7/8 from wifi-transfer-fix-plan.md (manual connect experiment) |
| `Lost association during DHCP wait` | Handshake succeeded but DHCP failed | Check glasses AP DHCP server |
| `No photos on glasses` | Glasses storage empty | Take a photo manually first |
