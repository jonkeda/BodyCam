# M14 Phase 2 — Bluetooth Glasses Buttons

## Goal

Receive button press events from BT smart glasses. Two mechanisms:
1. **AVRCP media controls** — glasses that expose a media play/pause button (most common)
2. **Custom GATT** — glasses that expose a BLE characteristic for their button

---

## AVRCP Media Button Provider

Most BT smart glasses expose their button as a media "play/pause" key via
AVRCP (Audio/Video Remote Control Profile). The OS sees this as a media
button press, the same as headphone inline controls.

### Android — MediaSession

On Android, a foreground `MediaSession` receives media button events. BodyCam
registers a `MediaSession` with a callback that forwards button events.

```csharp
namespace BodyCam.Platforms.Android.Input;

/// <summary>
/// Receives AVRCP media button events on Android via MediaSession.
/// Requires a foreground service or active MediaSession.
/// </summary>
public class AvrcpButtonProvider : IButtonInputProvider
{
    public string DisplayName => "BT Glasses (Media Button)";
    public string ProviderId => "avrcp";
    public bool IsAvailable => _mediaSession is not null;
    public bool IsActive { get; private set; }

    public event EventHandler<RawButtonEvent>? RawButtonEvent;
    public event EventHandler? Disconnected;

    private Android.Media.Session.MediaSession? _mediaSession;
    private MediaButtonCallback? _callback;

    public Task StartAsync(CancellationToken ct = default)
    {
        if (IsActive) return Task.CompletedTask;

        var context = Android.App.Application.Context;
        _mediaSession = new Android.Media.Session.MediaSession(context, "BodyCamButtons");

        _callback = new MediaButtonCallback(this);
        _mediaSession.SetCallback(_callback);

        // Set active so we receive media button events
        _mediaSession.Active = true;

        // Set flags to receive media buttons
        _mediaSession.SetFlags(
            Android.Media.Session.MediaSessionFlags.HandlesMediaButtons |
            Android.Media.Session.MediaSessionFlags.HandlesTransportControls);

        IsActive = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (!IsActive) return Task.CompletedTask;

        _mediaSession?.Release();
        _mediaSession = null;
        _callback = null;
        IsActive = false;
        return Task.CompletedTask;
    }

    internal void EmitRawEvent(RawButtonEventType type)
    {
        RawButtonEvent?.Invoke(this, new Services.Input.RawButtonEvent
        {
            ProviderId = ProviderId,
            EventType = type,
            TimestampMs = Environment.TickCount64,
        });
    }

    public void Dispose() => StopAsync().GetAwaiter().GetResult();

    /// <summary>
    /// MediaSession callback that receives media button events.
    /// </summary>
    private sealed class MediaButtonCallback : Android.Media.Session.MediaSession.Callback
    {
        private readonly AvrcpButtonProvider _provider;

        public MediaButtonCallback(AvrcpButtonProvider provider) => _provider = provider;

        public override bool OnMediaButtonEvent(Android.Content.Intent? mediaButtonIntent)
        {
            if (mediaButtonIntent is null) return false;

            var keyEvent = mediaButtonIntent.GetParcelableExtra(
                Android.Content.Intent.ExtraKeyEvent) as Android.Views.KeyEvent;

            if (keyEvent is null) return false;

            // We only care about KEYCODE_MEDIA_PLAY_PAUSE, KEYCODE_HEADSETHOOK
            if (keyEvent.KeyCode != Android.Views.Keycode.MediaPlayPause &&
                keyEvent.KeyCode != Android.Views.Keycode.Headsethook)
                return false;

            var eventType = keyEvent.Action switch
            {
                Android.Views.KeyEventActions.Down => RawButtonEventType.ButtonDown,
                Android.Views.KeyEventActions.Up => RawButtonEventType.ButtonUp,
                _ => (RawButtonEventType?)null,
            };

            if (eventType.HasValue)
                _provider.EmitRawEvent(eventType.Value);

            return true; // Consume the event
        }
    }
}
```

### Android Considerations

- **Foreground service required.** The `MediaSession` must be active and the app
  must have audio focus or a foreground notification to reliably receive media
  button events when the screen is off.
- **Competing apps.** Music players also register `MediaSession`. Android routes
  media buttons to the most recently active session. BodyCam should reclaim the
  session when its service starts.
- **KEYCODE_HEADSETHOOK vs KEYCODE_MEDIA_PLAY_PAUSE.** BT headsets typically send
  `HEADSETHOOK` for the inline button. Some glasses send `MEDIA_PLAY_PAUSE`. Handle
  both.
- **Double-tap from hardware.** Some BT devices do their own double-tap detection
  and send `KEYCODE_MEDIA_NEXT`. If we detect this, emit a `Click` event — the
  `GestureRecognizer` won't interfere since it's a different keycode.

### Windows — SystemMediaTransportControls

On Windows, `SystemMediaTransportControls` (SMTC) receives media button events.

```csharp
namespace BodyCam.Platforms.Windows.Input;

/// <summary>
/// Receives AVRCP media button events on Windows via SystemMediaTransportControls.
/// </summary>
public class AvrcpButtonProvider : IButtonInputProvider
{
    public string DisplayName => "BT Glasses (Media Button)";
    public string ProviderId => "avrcp";
    public bool IsAvailable => true; // SMTC always available on Windows
    public bool IsActive { get; private set; }

    public event EventHandler<RawButtonEvent>? RawButtonEvent;
    public event EventHandler? Disconnected;

    private Windows.Media.SystemMediaTransportControls? _smtc;

    public Task StartAsync(CancellationToken ct = default)
    {
        if (IsActive) return Task.CompletedTask;

        _smtc = Windows.Media.SystemMediaTransportControls.GetForCurrentView();
        _smtc.IsPlayEnabled = true;
        _smtc.IsPauseEnabled = true;
        _smtc.ButtonPressed += OnSmtcButtonPressed;

        IsActive = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (!IsActive || _smtc is null) return Task.CompletedTask;

        _smtc.ButtonPressed -= OnSmtcButtonPressed;
        _smtc = null;
        IsActive = false;
        return Task.CompletedTask;
    }

    private void OnSmtcButtonPressed(
        Windows.Media.SystemMediaTransportControls sender,
        Windows.Media.SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        if (args.Button != Windows.Media.SystemMediaTransportControlsButton.Play &&
            args.Button != Windows.Media.SystemMediaTransportControlsButton.Pause)
            return;

        // SMTC only gives us press events, not down/up separately
        RawButtonEvent?.Invoke(this, new Services.Input.RawButtonEvent
        {
            ProviderId = ProviderId,
            EventType = RawButtonEventType.Click,
            TimestampMs = Environment.TickCount64,
        });
    }

    public void Dispose() => StopAsync().GetAwaiter().GetResult();
}
```

### Windows Considerations

- **`GetForCurrentView()`** requires a CoreWindow context (WinUI/UWP). For .NET
  MAUI WinUI apps, this is available on the UI thread. If it throws, fall back to
  creating a background `MediaPlayer` and intercepting its SMTC.
- **SMTC only gives Click, not Down/Up.** Unlike Android's `KeyEvent` with actions,
  SMTC fires a single `ButtonPressed` event. The `GestureRecognizer` handles `Click`
  as an instantaneous down+up — single/double tap detection still works.
- **Alternative: Raw Input API.** For finer control (down/up events), hook
  `WM_APPCOMMAND` or use Raw Input to intercept `APPCOMMAND_MEDIA_PLAY_PAUSE`.
  This is a Phase 2 enhancement if SMTC proves insufficient.

---

## Custom GATT Button Provider

Some smart glasses expose a custom BLE GATT characteristic that changes value
when their button is pressed. This is more reliable than AVRCP (no competition
with music apps) but requires knowing the characteristic UUID for each glasses model.

```csharp
namespace BodyCam.Services.Input;

/// <summary>
/// Receives button events from BT glasses via a custom BLE GATT characteristic.
/// Requires the glasses to expose a known button characteristic UUID.
/// </summary>
public class GattButtonProvider : IButtonInputProvider
{
    private readonly IBleService _bleService;

    /// <summary>
    /// Known button characteristic UUIDs for supported glasses models.
    /// Add entries as new glasses are tested.
    /// </summary>
    private static readonly Dictionary<string, GlassesButtonProfile> KnownProfiles = new()
    {
        // Example: Chinese BT glasses model X
        // ["GlassesModelX"] = new GlassesButtonProfile
        // {
        //     ServiceUuid = Guid.Parse("0000ffe0-0000-1000-8000-00805f9b34fb"),
        //     CharacteristicUuid = Guid.Parse("0000ffe1-0000-1000-8000-00805f9b34fb"),
        //     PressedValue = 0x01,
        //     ReleasedValue = 0x00,
        // },
    };

    public string DisplayName => "BT Glasses (Button)";
    public string ProviderId => "gatt-button";
    public bool IsAvailable => _connectedProfile is not null;
    public bool IsActive { get; private set; }

    public event EventHandler<RawButtonEvent>? RawButtonEvent;
    public event EventHandler? Disconnected;

    private GlassesButtonProfile? _connectedProfile;
    private IDisposable? _subscription;

    public GattButtonProvider(IBleService bleService)
    {
        _bleService = bleService;
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (IsActive) return;

        // Scan connected BLE devices for a known button characteristic
        var devices = await _bleService.GetConnectedDevicesAsync(ct);

        foreach (var device in devices)
        {
            foreach (var (modelName, profile) in KnownProfiles)
            {
                var characteristic = await _bleService.GetCharacteristicAsync(
                    device.Id, profile.ServiceUuid, profile.CharacteristicUuid, ct);

                if (characteristic is null) continue;

                // Found a matching glasses model — subscribe to notifications
                _connectedProfile = profile;
                _subscription = await _bleService.SubscribeAsync(
                    characteristic, OnCharacteristicChanged, ct);

                IsActive = true;
                return;
            }
        }
    }

    public Task StopAsync()
    {
        _subscription?.Dispose();
        _subscription = null;
        _connectedProfile = null;
        IsActive = false;
        return Task.CompletedTask;
    }

    private void OnCharacteristicChanged(byte[] value)
    {
        if (_connectedProfile is null || value.Length == 0) return;

        var eventType = value[0] == _connectedProfile.PressedValue
            ? RawButtonEventType.ButtonDown
            : value[0] == _connectedProfile.ReleasedValue
                ? RawButtonEventType.ButtonUp
                : (RawButtonEventType?)null;

        if (eventType is null) return;

        RawButtonEvent?.Invoke(this, new Services.Input.RawButtonEvent
        {
            ProviderId = ProviderId,
            EventType = eventType.Value,
            TimestampMs = Environment.TickCount64,
        });
    }

    public void Dispose()
    {
        _subscription?.Dispose();
    }
}

/// <summary>
/// BLE profile for a specific glasses model's button characteristic.
/// </summary>
public sealed class GlassesButtonProfile
{
    public required Guid ServiceUuid { get; init; }
    public required Guid CharacteristicUuid { get; init; }
    public required byte PressedValue { get; init; }
    public required byte ReleasedValue { get; init; }
}
```

### Discovery Flow

```
1. User pairs glasses via phone's BT settings (or M4 pairing flow)
2. BodyCam's BleService detects connected BLE device
3. GattButtonProvider scans the device's GATT services
4. If a known button characteristic UUID is found → subscribe to notifications
5. Characteristic value changes → RawButtonEvent emitted
6. GestureRecognizer → ActionMap → MainViewModel
```

### Adding New Glasses Models

When testing with a new glasses model:

1. Use a BLE scanner app (nRF Connect) to find the glasses' GATT services
2. Look for a characteristic that changes value on button press
3. Note the service UUID, characteristic UUID, pressed/released byte values
4. Add an entry to `KnownProfiles`

```csharp
// Example: Adding support for "BrightView V2" glasses
KnownProfiles["BrightView-V2"] = new GlassesButtonProfile
{
    ServiceUuid = Guid.Parse("12345678-1234-1234-1234-123456789abc"),
    CharacteristicUuid = Guid.Parse("12345678-1234-1234-1234-123456789abd"),
    PressedValue = 0x01,
    ReleasedValue = 0x00,
};
```

### IBleService

The `GattButtonProvider` depends on an `IBleService` that abstracts BLE operations.
This is a thin wrapper around platform BLE APIs (Android BLE, Windows.Devices.Bluetooth).

```csharp
namespace BodyCam.Services;

/// <summary>
/// Minimal BLE service interface for GATT operations.
/// Platform-specific implementation per target.
/// </summary>
public interface IBleService
{
    /// <summary>Get currently connected BLE devices.</summary>
    Task<IReadOnlyList<BleDevice>> GetConnectedDevicesAsync(CancellationToken ct = default);

    /// <summary>Get a GATT characteristic from a connected device.</summary>
    Task<BleCharacteristic?> GetCharacteristicAsync(
        string deviceId, Guid serviceUuid, Guid characteristicUuid,
        CancellationToken ct = default);

    /// <summary>Subscribe to GATT characteristic notifications.</summary>
    Task<IDisposable> SubscribeAsync(
        BleCharacteristic characteristic, Action<byte[]> onChanged,
        CancellationToken ct = default);
}

public sealed class BleDevice
{
    public required string Id { get; init; }
    public required string Name { get; init; }
}

public sealed class BleCharacteristic
{
    public required string DeviceId { get; init; }
    public required Guid ServiceUuid { get; init; }
    public required Guid CharacteristicUuid { get; init; }
}
```

---

## Multi-Button Glasses

Some glasses have multiple buttons (e.g., a touchpad with swipe zones, separate
action + volume buttons). The `RawButtonEvent.ButtonId` field handles this:

```csharp
// Glasses with two buttons would emit:
new RawButtonEvent { ProviderId = "gatt-button", ButtonId = "action", ... }
new RawButtonEvent { ProviderId = "gatt-button", ButtonId = "volume", ... }
```

The `GestureRecognizer` tracks state per `(ProviderId, ButtonId)` pair, so each
button gets independent gesture detection. The `ActionMap` can map each button's
gestures independently:

```csharp
actionMap.SetAction("gatt-button:action", ButtonGesture.SingleTap, ButtonAction.Look);
actionMap.SetAction("gatt-button:volume", ButtonGesture.SingleTap, ButtonAction.ToggleSession);
```

---

## BT Connection Lifecycle

Button providers should handle BT disconnection/reconnection gracefully:

1. **Disconnection.** When the glasses BT connection drops, the provider raises
   `Disconnected`. The `ButtonInputManager` unsubscribes from events. The provider
   sets `IsAvailable = false`.
2. **Reconnection.** The BLE service detects the device reconnecting and notifies
   the `ButtonInputManager`. The manager calls `StartAsync()` again on the provider.
3. **App resume.** When the app comes to foreground after being backgrounded, the
   `ButtonInputManager` re-checks provider availability and restarts any that were
   lost.

```csharp
// In ButtonInputManager — called by App.OnResume()
public async Task RefreshProvidersAsync(CancellationToken ct = default)
{
    foreach (var provider in _providers)
    {
        if (provider.IsAvailable && !provider.IsActive)
        {
            provider.RawButtonEvent += OnRawButtonEvent;
            provider.Disconnected += OnProviderDisconnected;
            await provider.StartAsync(ct);
        }
    }
}
```
