# M14 — BLE Remote Controls (BTHome Protocol)

## Goal

Receive button and scroll events from BLE remote controls that use the **BTHome v2**
protocol. The primary target is the **Shelly BLU Remote Control ZB**, but the design
supports any BTHome-compatible BLE button/remote — Shelly BLU Button1, BLU Button
Tough1, DIY BTHome sensors, and future devices.

---

## Why BTHome?

BTHome is an open standard for BLE sensor/button advertisements, sponsored by Shelly
(Allterco Robotics). It uses BLE advertising — the phone passively scans for broadcasts
rather than establishing a GATT connection. This means:

- **No pairing required** — just scan and recognize the device
- **Low power** — the remote sleeps until a button is pressed, then broadcasts
- **Standardized events** — button press/double_press/long_press/hold_press are defined
  in the protocol, along with dimmer rotation events
- **Multi-device** — the phone can listen to multiple BTHome remotes simultaneously
- **Cross-platform** — BLE scanning works on both Android and Windows

The BTHome UUID `0xFCD2` is the service data identifier. Any device advertising with
this UUID can be parsed by a single generic scanner.

---

## Shelly BLU Remote Control ZB — Hardware

| Feature | Details |
|---------|---------|
| **Model** | SBRC-005B |
| **BTHome Device ID** | 0x0009 |
| **Protocols** | BLE 5.0 (LE) + Zigbee 802.15.4 |
| **Buttons** | 2 navigation (`<`, `>`) + 2 ring buttons (left, right) |
| **Scroll wheel** | Precision scroller, up/down rotation with step count |
| **Accelerometer** | Yes — gesture control (tilt/wave), angle measurement on nav long-press |
| **Channels** | 4 channels with dedicated LEDs (for Shelly ecosystem; we ignore channels) |
| **Feedback** | 4 LEDs + buzzer |
| **Battery** | 2x AAA, ~2 years battery life |
| **Range** | 10m indoors / 30m outdoors |
| **Size** | 129 x 47 x 24 mm, 85g with batteries |
| **MCU** | EFR32MG27 (Silicon Labs) |

### Button Layout

```
        ┌───────────────────┐
        │    ◄  LED LED  ►  │   ← Navigation buttons (< >)
        │                   │
        │   ┌─────────────┐ │
        │   │  LEFT  RIGHT│ │   ← Ring buttons
        │   └─────────────┘ │
        │                   │
        │    ╔═══════════╗  │
        │    ║  SCROLL   ║  │   ← Scroll wheel (rotate up/down)
        │    ╚═══════════╝  │
        │                   │
        └───────────────────┘
```

### Useful Inputs for BodyCam

| Physical Input | BTHome Event | Proposed BodyCam `ButtonId` |
|----------------|-------------|----------------------------|
| Left ring button press | `button[0] = press` | `"left-ring"` |
| Left ring button double press | `button[0] = double_press` | `"left-ring"` |
| Left ring button long press | `button[0] = long_press` | `"left-ring"` |
| Right ring button press | `button[1] = press` | `"right-ring"` |
| Right ring button double press | `button[1] = double_press` | `"right-ring"` |
| Right ring button long press | `button[1] = long_press` | `"right-ring"` |
| Nav `<` button press | `button[2] = press` | `"nav-left"` |
| Nav `>` button press | `button[3] = press` | `"nav-right"` |
| Scroll wheel up | `dimmer = rotate_right N steps` | `"scroll"` |
| Scroll wheel down | `dimmer = rotate_left N steps` | `"scroll"` |

We ignore the 4-channel system (that's for Shelly's own device control). We treat
all button events as BodyCam input regardless of the active Shelly channel.

---

## BTHome v2 Protocol — Relevant Subset

### BLE Advertisement Structure

```
AD Element: Service Data (UUID 0xFCD2)
  ├── BTHome Device Info byte (0x44 = version 2, trigger-based)
  ├── Object ID 0x3A (button) + event byte
  │     0x00 = none, 0x01 = press, 0x02 = double_press
  │     0x03 = triple_press, 0x04 = long_press, 0x80 = hold_press
  ├── Object ID 0x3A (button) + event byte  ← second button
  ├── Object ID 0x3A (button) + event byte  ← third button
  ├── Object ID 0x3A (button) + event byte  ← fourth button
  └── Object ID 0x3C (dimmer) + direction byte + step count byte
        0x01 = rotate_left, 0x02 = rotate_right
```

### Key Protocol Details

- **Trigger-based device**: Bit 2 of device info byte is set — the remote only
  advertises when a button is pressed, not periodically
- **Multiple buttons**: Multiple `0x3A` objects in sequence, one per button.
  `0x3A 0x00` means "no event for this button" (used when only one button fires)
- **Pre-recognized gestures**: The remote's firmware performs gesture detection —
  it sends `press`, `double_press`, `long_press` directly. No need for our
  `GestureRecognizer` to detect timing
- **Dimmer events**: `0x3C` object with direction (0x01=left, 0x02=right) and
  step count (uint8). Full scroll wheel rotation ≈ 3 seconds of movement
- **Packet ID**: Optional `0x00` object for deduplication. Same packet ID = same
  event, skip it
- **Encryption**: Optional AES-128 CCM. The remote can be configured with a
  pre-shared key. For BodyCam, we support unencrypted first and add encryption
  support later if needed

### Parsing Example

```
Raw service data: D2FC 44 3A00 3A01 3A00 3A00 3C0000
                  │    │  │    │    │    │    └── dimmer: none
                  │    │  │    │    │    └── button[3]: none (nav >)
                  │    │  │    │    └── button[2]: none (nav <)
                  │    │  │    └── button[1]: press (right ring)
                  │    │  └── button[0]: none (left ring)
                  │    └── device info: v2, trigger-based
                  └── UUID 0xFCD2 (little-endian)
```

---

## Architecture — Provider Design

### Key Design Decision: Pre-Recognized Gestures

The existing M14 architecture assumes providers emit **raw** button events
(down/up/click) and the centralized `GestureRecognizer` converts them to gestures.
BTHome remotes break this assumption — the firmware already sends gesture-level
events (`press`, `double_press`, `long_press`).

**Solution:** The `BtHomeButtonProvider` emits **pre-recognized gestures** directly
to the `ButtonInputManager`, bypassing the `GestureRecognizer`. This requires a
small extension to `IButtonInputProvider`:

```csharp
public interface IButtonInputProvider : IDisposable
{
    // ... existing members ...

    /// <summary>
    /// Raised when a raw button event is detected.
    /// Fed through GestureRecognizer by ButtonInputManager.
    /// </summary>
    event EventHandler<RawButtonEvent>? RawButtonEvent;

    /// <summary>
    /// Optional: Raised when the provider performs its own gesture recognition
    /// (e.g. BTHome firmware). ButtonInputManager routes these directly to the
    /// ActionMap, bypassing GestureRecognizer. Providers that don't do firmware
    /// gesture recognition leave this null / never raise it.
    /// </summary>
    event EventHandler<ButtonGestureEvent>? PreRecognizedGesture;

    // ... rest unchanged ...
}
```

**ButtonInputManager update:**

```csharp
// In StartAsync, also subscribe to PreRecognizedGesture
provider.PreRecognizedGesture += OnPreRecognizedGesture;

private void OnPreRecognizedGesture(object? sender, ButtonGestureEvent gesture)
{
    // Skip GestureRecognizer — go straight to ActionMap
    var action = _actionMap.GetAction(gesture.ProviderId, gesture.Gesture);
    if (action == ButtonAction.None) return;

    ActionTriggered?.Invoke(this, new ButtonActionEvent
    {
        Action = action,
        SourceProviderId = gesture.ProviderId,
        SourceGesture = gesture.Gesture,
        TimestampMs = gesture.TimestampMs,
    });
}
```

This avoids the 300ms single-tap delay that the `GestureRecognizer` introduces
(it waits for a possible second tap). BTHome devices have already resolved the
gesture on-device.

### BLE Scanning

```
┌────────────────────────────┐
│  BLE Scanner               │  Platform BLE API (Android BluetoothLeScanner,
│  (passive scan for FCD2)   │  Windows BluetoothLEAdvertisementWatcher)
└─────────────┬──────────────┘
              │ BTHome advertisement received
              ▼
┌────────────────────────────┐
│  BTHome Parser             │  Parse service data bytes
│  → Extract button events   │  → Object ID 0x3A + event type
│  → Extract dimmer events   │  → Object ID 0x3C + direction + steps
│  → Dedup via packet ID     │
└─────────────┬──────────────┘
              │ Parsed events
              ▼
┌────────────────────────────┐
│  BtHomeButtonProvider      │  Maps BTHome events to ButtonGestureEvent
│  : IButtonInputProvider    │  Emits via PreRecognizedGesture
└─────────────┬──────────────┘
              │ ButtonGestureEvent
              ▼
┌────────────────────────────┐
│  ButtonInputManager        │  ActionMap lookup → ButtonAction
└─────────────┬──────────────┘
              │ ActionTriggered event
              ▼
┌────────────────────────────┐
│  MainViewModel             │  Execute command
└────────────────────────────┘
```

---

## Implementation

### BTHome Parser (Shared)

```csharp
namespace BodyCam.Services.Input;

/// <summary>
/// Parses BTHome v2 BLE advertisement service data into structured events.
/// </summary>
public static class BtHomeParser
{
    public const ushort BtHomeUuid = 0xFCD2;

    /// <summary>
    /// Parse BTHome v2 service data bytes (after the UUID).
    /// Returns button events, dimmer events, and metadata.
    /// </summary>
    public static BtHomePayload? Parse(ReadOnlySpan<byte> serviceData)
    {
        if (serviceData.Length < 1) return null;

        var deviceInfo = serviceData[0];
        var version = (deviceInfo >> 5) & 0x07;
        if (version != 2) return null; // Only BTHome v2

        var encrypted = (deviceInfo & 0x01) != 0;
        if (encrypted) return null; // Encryption not supported yet

        var payload = new BtHomePayload();
        var offset = 1;

        while (offset < serviceData.Length)
        {
            var objectId = serviceData[offset++];

            switch (objectId)
            {
                case 0x00: // Packet ID
                    if (offset >= serviceData.Length) return payload;
                    payload.PacketId = serviceData[offset++];
                    break;

                case 0x3A: // Button event
                    if (offset >= serviceData.Length) return payload;
                    payload.ButtonEvents.Add((BtHomeButtonEvent)serviceData[offset++]);
                    break;

                case 0x3C: // Dimmer event
                    if (offset + 1 >= serviceData.Length) return payload;
                    var direction = serviceData[offset++];
                    var steps = serviceData[offset++];
                    payload.DimmerEvents.Add(new BtHomeDimmerEvent(direction, steps));
                    break;

                default:
                    // Unknown object ID — stop parsing (BTHome spec: objects
                    // must be in numerical order; unknown = newer version)
                    return payload;
            }
        }

        return payload;
    }
}

public sealed class BtHomePayload
{
    public byte? PacketId { get; set; }
    public List<BtHomeButtonEvent> ButtonEvents { get; } = new();
    public List<BtHomeDimmerEvent> DimmerEvents { get; } = new();
}

public enum BtHomeButtonEvent : byte
{
    None = 0x00,
    Press = 0x01,
    DoublePress = 0x02,
    TriplePress = 0x03,
    LongPress = 0x04,
    LongDoublePress = 0x05,
    LongTriplePress = 0x06,
    HoldPress = 0x80,
}

public readonly record struct BtHomeDimmerEvent(byte Direction, byte Steps)
{
    public bool IsRotateLeft => Direction == 0x01;
    public bool IsRotateRight => Direction == 0x02;
}
```

### Device Profiles

Different BTHome remotes have different button counts and layouts. A profile
maps button indices to human-readable `ButtonId` values.

```csharp
namespace BodyCam.Services.Input;

/// <summary>
/// Maps a BTHome device's button indices to logical button names.
/// </summary>
public sealed class BtHomeDeviceProfile
{
    /// <summary>Display name for the device type.</summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// BTHome device type ID (from object 0xF0), or null to match by name.
    /// </summary>
    public ushort? DeviceTypeId { get; init; }

    /// <summary>
    /// BLE local name prefix to match (e.g. "SBRC" for Shelly BLU Remote).
    /// </summary>
    public string? LocalNamePrefix { get; init; }

    /// <summary>
    /// Maps button index (0-based, from sequential 0x3A objects) to ButtonId.
    /// </summary>
    public required string[] ButtonNames { get; init; }

    /// <summary>
    /// Whether this device has a dimmer/scroll wheel.
    /// </summary>
    public bool HasDimmer { get; init; }

    /// <summary>ButtonId for dimmer events.</summary>
    public string DimmerButtonId { get; init; } = "scroll";
}

/// <summary>
/// Known BTHome device profiles.
/// </summary>
public static class BtHomeProfiles
{
    public static readonly BtHomeDeviceProfile ShellyBluRemoteZb = new()
    {
        DisplayName = "Shelly BLU Remote Control ZB",
        DeviceTypeId = 0x0009,
        LocalNamePrefix = "SBRC",
        ButtonNames = ["left-ring", "right-ring", "nav-left", "nav-right"],
        HasDimmer = true,
        DimmerButtonId = "scroll",
    };

    public static readonly BtHomeDeviceProfile ShellyBluButton1 = new()
    {
        DisplayName = "Shelly BLU Button1",
        LocalNamePrefix = "SBBT",
        ButtonNames = ["primary"],
        HasDimmer = false,
    };

    public static readonly BtHomeDeviceProfile ShellyBluButtonTough1 = new()
    {
        DisplayName = "Shelly BLU Button Tough1",
        LocalNamePrefix = "SBDW",
        ButtonNames = ["primary"],
        HasDimmer = false,
    };

    /// <summary>
    /// Generic BTHome button device — used when no specific profile matches.
    /// Maps button indices to "button-0", "button-1", etc.
    /// </summary>
    public static readonly BtHomeDeviceProfile Generic = new()
    {
        DisplayName = "BTHome Remote",
        ButtonNames = ["button-0", "button-1", "button-2", "button-3",
                        "button-4", "button-5", "button-6", "button-7"],
        HasDimmer = true,
    };

    /// <summary>All known profiles, checked in order.</summary>
    public static readonly BtHomeDeviceProfile[] All =
    [
        ShellyBluRemoteZb,
        ShellyBluButton1,
        ShellyBluButtonTough1,
    ];

    /// <summary>
    /// Find the best matching profile for a BLE device.
    /// </summary>
    public static BtHomeDeviceProfile Match(string? localName, ushort? deviceTypeId)
    {
        foreach (var profile in All)
        {
            if (profile.DeviceTypeId is not null && deviceTypeId == profile.DeviceTypeId)
                return profile;
            if (profile.LocalNamePrefix is not null && localName?.StartsWith(profile.LocalNamePrefix) == true)
                return profile;
        }
        return Generic;
    }
}
```

### BtHomeButtonProvider

```csharp
namespace BodyCam.Services.Input;

/// <summary>
/// Receives button/dimmer events from BTHome v2 BLE devices via passive scanning.
/// Supports multiple BTHome remotes simultaneously.
/// Emits pre-recognized gestures (firmware does gesture detection).
/// </summary>
public class BtHomeButtonProvider : IButtonInputProvider
{
    public string DisplayName => "BLE Remote (BTHome)";
    public string ProviderId => "bthome";
    public bool IsAvailable => _bleAvailable;
    public bool IsActive { get; private set; }

    public event EventHandler<RawButtonEvent>? RawButtonEvent;
    public event EventHandler<ButtonGestureEvent>? PreRecognizedGesture;
    public event EventHandler? Disconnected;

    private bool _bleAvailable;
    private byte? _lastPacketId;

    // Platform-specific BLE scanner — injected or set via partial class
    private IDisposable? _scanSubscription;

    public Task StartAsync(CancellationToken ct = default)
    {
        if (IsActive) return Task.CompletedTask;

        // Start BLE scan filtered to BTHome UUID 0xFCD2
        _scanSubscription = StartBleScanning();
        _bleAvailable = _scanSubscription is not null;
        IsActive = _bleAvailable;

        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _scanSubscription?.Dispose();
        _scanSubscription = null;
        IsActive = false;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called when a BTHome BLE advertisement is received.
    /// </summary>
    internal void OnAdvertisementReceived(
        string? localName, ushort? deviceTypeId, ReadOnlySpan<byte> serviceData)
    {
        var payload = BtHomeParser.Parse(serviceData);
        if (payload is null) return;

        // Deduplicate by packet ID
        if (payload.PacketId is not null)
        {
            if (payload.PacketId == _lastPacketId) return;
            _lastPacketId = payload.PacketId;
        }

        var profile = BtHomeProfiles.Match(localName, deviceTypeId);
        var now = Environment.TickCount64;

        // Emit button events as pre-recognized gestures
        for (int i = 0; i < payload.ButtonEvents.Count; i++)
        {
            var evt = payload.ButtonEvents[i];
            if (evt == BtHomeButtonEvent.None) continue;

            var buttonId = i < profile.ButtonNames.Length
                ? profile.ButtonNames[i]
                : $"button-{i}";

            var gesture = MapBtHomeGesture(evt);
            if (gesture is null) continue;

            PreRecognizedGesture?.Invoke(this, new ButtonGestureEvent
            {
                ProviderId = ProviderId,
                ButtonId = buttonId,
                Gesture = gesture.Value,
                TimestampMs = now,
            });
        }

        // Emit scroll/dimmer events as clicks with direction in ButtonId
        foreach (var dimmer in payload.DimmerEvents)
        {
            if (dimmer.Direction == 0) continue;

            var scrollId = dimmer.IsRotateRight ? "scroll-up" : "scroll-down";

            // Emit one click per step for fine-grained control,
            // or a single click with step count metadata.
            // For simplicity: single click per event, step count ignored initially.
            RawButtonEvent?.Invoke(this, new RawButtonEvent
            {
                ProviderId = ProviderId,
                EventType = RawButtonEventType.Click,
                TimestampMs = now,
                ButtonId = scrollId,
            });
        }
    }

    private static ButtonGesture? MapBtHomeGesture(BtHomeButtonEvent evt) => evt switch
    {
        BtHomeButtonEvent.Press => ButtonGesture.SingleTap,
        BtHomeButtonEvent.DoublePress => ButtonGesture.DoubleTap,
        BtHomeButtonEvent.TriplePress => ButtonGesture.DoubleTap, // Map triple → double for now
        BtHomeButtonEvent.LongPress => ButtonGesture.LongPress,
        BtHomeButtonEvent.HoldPress => ButtonGesture.LongPress,
        _ => null,
    };

    public void Dispose() => StopAsync().GetAwaiter().GetResult();
}
```

### Platform-Specific BLE Scanning

#### Android

```csharp
// Platforms/Android/Input/BtHomeButtonProvider.Android.cs
// (partial class or platform-specific StartBleScanning implementation)

private IDisposable? StartBleScanning()
{
    var bluetoothManager = Android.App.Application.Context
        .GetSystemService(Android.Content.Context.BluetoothService)
        as Android.Bluetooth.BluetoothManager;

    var scanner = bluetoothManager?.Adapter?.BluetoothLeScanner;
    if (scanner is null) return null;

    // Filter for BTHome UUID 0xFCD2
    var filter = new Android.Bluetooth.LE.ScanFilter.Builder()
        .SetServiceData(
            Android.OS.ParcelUuid.FromString("0000fcd2-0000-1000-8000-00805f9b34fb"),
            null)
        .Build();

    var settings = new Android.Bluetooth.LE.ScanSettings.Builder()
        .SetScanMode(Android.Bluetooth.LE.ScanMode.LowLatency)
        .Build();

    var callback = new BtHomeScanCallback(this);
    scanner.StartScan([filter], settings, callback);

    return new ScanDisposable(scanner, callback);
}
```

#### Windows

```csharp
// Platforms/Windows/Input/BtHomeButtonProvider.Windows.cs

private IDisposable? StartBleScanning()
{
    var watcher = new Windows.Devices.Bluetooth.Advertisement
        .BluetoothLEAdvertisementWatcher();

    // Filter for BTHome service data UUID 0xFCD2
    watcher.AdvertisementFilter.Advertisement.ServiceData.Add(
        new Windows.Devices.Bluetooth.Advertisement.BluetoothLEAdvertisementDataSection
        {
            DataType = 0x16, // Service Data - 16-bit UUID
        });

    watcher.Received += (sender, args) =>
    {
        // Extract service data for UUID 0xFCD2
        foreach (var section in args.Advertisement.GetSectionsByType(0x16))
        {
            var reader = Windows.Storage.Streams.DataReader.FromBuffer(section.Data);
            var bytes = new byte[section.Data.Length];
            reader.ReadBytes(bytes);

            // First 2 bytes are the UUID (little-endian)
            if (bytes.Length < 3) continue;
            var uuid = (ushort)(bytes[0] | (bytes[1] << 8));
            if (uuid != BtHomeParser.BtHomeUuid) continue;

            var serviceData = bytes.AsSpan(2); // Skip UUID bytes
            OnAdvertisementReceived(
                args.Advertisement.LocalName,
                null, // deviceTypeId parsed from payload if present
                serviceData);
        }
    };

    watcher.Start();
    return new WatcherDisposable(watcher);
}
```

---

## Default Action Mapping for Shelly BLU Remote

```csharp
// Default mappings — customizable via settings
actionMap.SetAction("bthome:left-ring",  ButtonGesture.SingleTap, ButtonAction.Look);
actionMap.SetAction("bthome:left-ring",  ButtonGesture.DoubleTap, ButtonAction.Read);
actionMap.SetAction("bthome:left-ring",  ButtonGesture.LongPress, ButtonAction.Photo);

actionMap.SetAction("bthome:right-ring", ButtonGesture.SingleTap, ButtonAction.ToggleSession);
actionMap.SetAction("bthome:right-ring", ButtonGesture.DoubleTap, ButtonAction.Find);
actionMap.SetAction("bthome:right-ring", ButtonGesture.LongPress, ButtonAction.ToggleSleepActive);

// Nav buttons for future use (camera switching, audio source selection)
actionMap.SetAction("bthome:nav-left",   ButtonGesture.SingleTap, ButtonAction.None);
actionMap.SetAction("bthome:nav-right",  ButtonGesture.SingleTap, ButtonAction.None);

// Scroll wheel — could map to volume, zoom, or other continuous actions
// (Requires adding continuous actions to ButtonAction enum in the future)
```

### Mapping Rationale

| Button | Gesture | Action | Why |
|--------|---------|--------|-----|
| Left ring | Press | **Look** | Primary vision action — quick press to see |
| Left ring | Double | **Read** | Secondary vision action — read text |
| Left ring | Long | **Photo** | Capture-and-describe — deliberate action |
| Right ring | Press | **Toggle Session** | Start/stop AI conversation |
| Right ring | Double | **Find** | Search for objects |
| Right ring | Long | **Toggle Sleep** | Put AI to sleep / wake up |
| Nav `<`/`>` | Press | (reserved) | Future: switch camera, audio source |
| Scroll | Rotate | (reserved) | Future: volume, brightness |

The left ring handles vision actions (what the camera sees). The right ring
handles session lifecycle (conversation control). Navigation buttons and scroll
are reserved for future peripheral control.

---

## Other Compatible BLE Remotes

The BTHome protocol is an open standard. These devices also work with the same
`BtHomeButtonProvider`:

| Device | Buttons | Scroll | Notes |
|--------|---------|--------|-------|
| **Shelly BLU Button1** | 1 | No | Tiny single-button fob, ~€10 |
| **Shelly BLU Button Tough1** | 1 | No | Waterproof (IP68) single button |
| **DIY BTHome buttons** | Varies | Varies | ESP32/nRF52 custom builds |
| **Any BTHome-compatible device** | Varies | Varies | Open standard — growing ecosystem |

### Single-Button Remotes (BLU Button1)

For single-button devices, all three gestures are available on one button:

```csharp
actionMap.SetAction("bthome:primary", ButtonGesture.SingleTap, ButtonAction.Look);
actionMap.SetAction("bthome:primary", ButtonGesture.DoubleTap, ButtonAction.ToggleSession);
actionMap.SetAction("bthome:primary", ButtonGesture.LongPress, ButtonAction.Photo);
```

### Non-BTHome BLE Remotes

Generic BLE remotes that **don't** use BTHome (e.g. cheap Amazon/AliExpress BLE
clickers, camera shutter remotes) typically behave as HID devices or send
custom GATT notifications. These are handled by the existing `GattButtonProvider`
(from [bt-buttons.md](bt-buttons.md)), not by this BTHome provider.

The most common cheap BLE camera shutter remote sends volume-up key events
(as a BLE HID keyboard), which would be caught by the `VolumeButtonProvider`
or `AvrcpButtonProvider` instead.

---

## IButtonInputProvider Interface Extension

To support pre-recognized gestures from BTHome (and potentially from smart glasses
firmware that does its own gesture detection), the `IButtonInputProvider` interface
needs one addition:

```csharp
/// <summary>
/// Optional: Raised when the provider performs its own gesture recognition
/// (e.g. BTHome firmware, smart glasses firmware). ButtonInputManager routes
/// these directly to the ActionMap, bypassing GestureRecognizer.
///
/// Providers that emit raw down/up events only use RawButtonEvent.
/// Providers that have firmware gesture recognition use PreRecognizedGesture.
/// A provider may use both (e.g. scroll wheel as raw clicks + buttons as gestures).
/// </summary>
event EventHandler<ButtonGestureEvent>? PreRecognizedGesture;
```

This is the **only change** to the existing M14 abstraction. All other interfaces,
enums, and classes remain unchanged.

---

## Phase Integration

This design fits into **M14 Phase 2** (BT Glasses Buttons). The `BtHomeButtonProvider`
is registered alongside `AvrcpButtonProvider` and `GattButtonProvider`:

```csharp
// MauiProgram.cs — BLE remote support
builder.Services.AddSingleton<IButtonInputProvider, BtHomeButtonProvider>();
```

Since `ButtonInputManager` aggregates all active providers simultaneously, the
BTHome remote works alongside glasses buttons, keyboard shortcuts, and volume keys.

### New Files

| File | Purpose |
|------|---------|
| `Services/Input/BtHomeParser.cs` | Parse BTHome v2 advertisement bytes |
| `Services/Input/BtHomeDeviceProfile.cs` | Device profiles (button name mapping) |
| `Services/Input/BtHomeButtonProvider.cs` | IButtonInputProvider for BTHome BLE devices |
| `Platforms/Android/Input/BtHomeBleScanner.cs` | Android BluetoothLeScanner wrapper |
| `Platforms/Windows/Input/BtHomeBleScanner.cs` | Windows BLE Advertisement Watcher |

### Dependencies

- Android: `Android.Bluetooth.LE` (built into Android SDK)
- Windows: `Windows.Devices.Bluetooth.Advertisement` (WinRT API, available in WinUI)
- No NuGet packages needed — both platforms have native BLE scanning APIs

---

## Permissions

### Android

```xml
<!-- AndroidManifest.xml -->
<uses-permission android:name="android.permission.BLUETOOTH_SCAN" />
<uses-permission android:name="android.permission.BLUETOOTH_CONNECT" />
<!-- For Android 11 and below: -->
<uses-permission android:name="android.permission.ACCESS_FINE_LOCATION" />
```

BLE scanning on Android 12+ requires `BLUETOOTH_SCAN` permission. On older versions,
`ACCESS_FINE_LOCATION` is needed. The provider checks and requests permissions in
`StartAsync()`, same pattern as `AndroidAudioInputService`.

### Windows

No special permissions needed. BLE scanning works without elevation on Windows 10+.
