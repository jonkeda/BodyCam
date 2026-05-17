# Phase 7 — Classic Bluetooth Audio Pairing

**Status:** Proposed  
**Depends on:** Phase 6 (BLE Audio — **in progress**)  
**Sibling phases:** [Phase 1](../phase-1-ble-discovery/overview.md), [Phase 2](../phase-2-windows-ble/overview.md), [Phase 3](../phase-3-windows-wifi/overview.md), [Phase 4](../phase-4-integration/overview.md), [Phase 5](../phase-5-wifi-joining/overview.md), [Phase 6](../phase-6-ble-audio/overview.md)

---

## Problem

The Phase 6 programmatic BLE pairing (`PairAsync()`) succeeds, but Windows
only creates a BLE GATT device — not a Classic Bluetooth audio endpoint.
`HasEndpointWithMac(mac)` returns `false` because no HFP/A2DP device with the
glasses' MAC address appears in the Windows audio device list.

The M01 glasses support two independent Bluetooth transports:

| Transport | Purpose | Pairing method |
|---|---|---|
| **BLE (Low Energy)** | Commands, notifications, GATT services | Programmatic via `PairAsync()` (Phase 2) |
| **BT Classic (HFP/A2DP)** | Live microphone (SCO), speaker output (A2DP) | Standard Bluetooth pairing via OS |

On Android/iOS, the SDK handles both connections transparently. On Windows,
Classic BT must be paired separately — but the glasses likely only advertise
Classic BT at power-on (pairing mode), not after a BLE connection is
established. Both connections must happen in the same pairing window.

---

## Solution

Pair Classic BT **simultaneously** with the BLE connection during the initial
connect flow. The glasses enter pairing mode at power-on and advertise both
BLE and Classic BT. Once either connection is established, the pairing window
may close — so both must be initiated before the BLE GATT connect completes.

### 7.1 — Parallel Classic BT discovery during BLE scan

When the BLE scanner finds the glasses (`M01 Pro_XXXX`), immediately kick off
a parallel Classic BT discovery for the same device. The glasses are
advertising both transports during their pairing window.

**Files:** `WindowsHeyCyanGlassesSession.cs`

```csharp
public async Task ConnectAsync(HeyCyanDevice device, CancellationToken ct)
{
    // Start Classic BT pairing in parallel — glasses are in pairing mode NOW,
    // and may leave it once BLE connects.
    var classicPairTask = TryPairClassicAsync(device.Address, ct);

    // Existing BLE GATT connection flow
    _bleDevice = await BluetoothLEDevice.FromBluetoothAddressAsync(address);
    // ... GATT service discovery, characteristic setup, notifications ...

    // Await Classic BT pairing result (may have completed already)
    var classicPaired = await classicPairTask;
    if (classicPaired)
        _log.LogInformation("Classic BT paired — audio endpoint should appear");
    else
        _log.LogWarning("Classic BT pairing failed — audio will be unavailable");

    State = HeyCyanState.Connected;
}
```

### 7.2 — Programmatic Classic BT discovery and pairing

Discover the glasses as a Classic BT device by MAC address and pair using
custom pairing (no system UI):

```csharp
private async Task<bool> TryPairClassicAsync(string bleMac, CancellationToken ct)
{
    try
    {
        // Classic BT MAC may differ from BLE MAC on dual-mode devices.
        // Try exact match first, then name-based match.
        var classicDevice = await FindClassicDeviceAsync(bleMac, ct);
        if (classicDevice is null)
            return false;

        if (classicDevice.DeviceInformation.Pairing.IsPaired)
            return true;  // Already paired from a previous session

        var customPairing = classicDevice.DeviceInformation.Pairing.Custom;
        customPairing.PairingRequested += (_, args) =>
        {
            // M01 uses "Just Works" — accept without PIN
            args.Accept();
        };

        var result = await customPairing.PairAsync(
            DevicePairingKinds.ConfirmOnly | DevicePairingKinds.ProvidePin)
            .AsTask(ct);

        return result.Status == DevicePairingResultStatus.Paired
            || result.Status == DevicePairingResultStatus.AlreadyPaired;
    }
    catch (Exception ex)
    {
        _log.LogWarning(ex, "Classic BT pairing attempt failed");
        return false;
    }
}

private async Task<BluetoothDevice?> FindClassicDeviceAsync(
    string bleMac, CancellationToken ct)
{
    // Strategy 1: Direct lookup by converting BLE MAC to Classic BT address
    // (on dual-mode devices these are often identical or differ by 1)
    var bleAddr = MacToUlong(bleMac);
    var device = await BluetoothDevice.FromBluetoothAddressAsync(bleAddr)
        .AsTask(ct);
    if (device is not null)
        return device;

    // Strategy 2: Enumerate unpaired Classic BT devices, match by name
    var aqsFilter = BluetoothDevice.GetDeviceSelectorFromPairingState(false);
    var devices = await DeviceInformation.FindAllAsync(aqsFilter)
        .AsTask(ct);
    var match = devices.FirstOrDefault(d =>
        d.Name.StartsWith("M01", StringComparison.OrdinalIgnoreCase));
    if (match is not null)
        return await BluetoothDevice.FromIdAsync(match.Id).AsTask(ct);

    return null;
}
```

**Key considerations:**

- **Timing is critical**: Classic BT discovery must start before or alongside
  BLE GATT connect. If BLE connects first, the glasses may leave pairing mode.
- **MAC address**: BLE and Classic BT addresses are often identical on
  dual-mode devices. `BluetoothDevice.FromBluetoothAddressAsync(bleAddr)` is
  the fastest path — no enumeration needed.
- **Already paired**: On subsequent connections, `IsPaired` is `true` and
  the Classic BT audio endpoint is created automatically by Windows. The
  parallel pairing attempt becomes a no-op.

### 7.3 — Monitor for audio endpoint appearance

After Classic BT pairing succeeds, the audio endpoint may take a few seconds
to appear. `HeyCyanAudioRouter` already handles this — on the `Connected`
event it checks `IsAvailable` and logs a warning if missing. Add a delayed
retry:

```csharp
// In HeyCyanAudioRouter.ApplyAsync, after initial IsAvailable check fails:
// Wait for Windows to create the audio endpoint after Classic BT pairing
for (int i = 0; i < 10 && !_outputProvider.IsAvailable; i++)
    await Task.Delay(2000, ct);

if (_inputProvider.IsAvailable)
    await _input.SetActiveProviderAsync("heycyan-glasses");
if (_outputProvider.IsAvailable)
    await _output.SetActiveProviderAsync("heycyan-glasses");
```

### 7.4 — Remember paired state

Once Classic BT pairing succeeds, Windows remembers it. On subsequent
connections the flow is:
1. User taps "Connect" → BLE GATT connects
2. `TryPairClassicAsync` sees `IsPaired == true` → returns immediately
3. Windows auto-connects Classic BT audio → endpoint available
4. `HeyCyanAudioRouter` registers and selects glasses providers

No repeated pairing or user interaction needed.

---

## How It Works (User Perspective)

### First time connecting
1. Power on glasses (they enter pairing mode automatically)
2. Tap "Connect" in BodyCam app
3. App pairs both BLE (commands) and Classic BT (audio) simultaneously
4. Glasses mic/speaker appear in dropdowns and are auto-selected

### Subsequent connections
1. Power on glasses
2. Tap "Connect" in BodyCam app
3. Both connections re-establish automatically (Windows remembers pairing)

### If audio pairing fails
The app shows a prompt to power-cycle the glasses and reconnect.
As a last resort, manual pairing via Windows Settings is available.

### Troubleshooting

| Issue | Fix |
|---|---|
| Glasses paired for BLE but no audio | Power off glasses, wait 3s, power on, reconnect immediately |
| Paired but no audio endpoint | Remove device in Windows Settings, restart Bluetooth, reconnect |
| Audio crackles or drops | Move closer to PC. Disable other BT audio devices. |
| Glasses already paired on phone | Unpair from phone first — Classic BT may only pair with one device |

---

## Acceptance

- [ ] Classic BT pairing runs in parallel with BLE GATT connect
- [ ] Programmatic pairing succeeds on first connect (no user interaction)
- [ ] Subsequent connections auto-pair (Windows remembers)
- [ ] After Classic BT pairing, glasses mic/speaker appear in dropdowns
- [ ] Auto-selected after endpoint detected
- [ ] Verified on Windows 10 and Windows 11
