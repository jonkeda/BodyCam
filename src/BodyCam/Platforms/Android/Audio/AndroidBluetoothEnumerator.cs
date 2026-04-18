using Android.Bluetooth;
using Android.Content;
using BodyCam.Services.Audio;

namespace BodyCam.Platforms.Android.Audio;

/// <summary>
/// Enumerates paired Bluetooth audio devices on Android
/// and registers them as IAudioInputProvider instances.
/// </summary>
public sealed class AndroidBluetoothEnumerator : IDisposable
{
    private readonly AudioInputManager _manager;
    private readonly AppSettings _settings;
    private readonly Context _context;
    private BluetoothHeadsetReceiver? _receiver;

    public AndroidBluetoothEnumerator(
        AudioInputManager manager,
        AppSettings settings)
    {
        _manager = manager;
        _settings = settings;
        _context = global::Android.App.Application.Context;
    }

    /// <summary>
    /// Scan paired BT devices for HFP-capable devices and register them.
    /// </summary>
    public void ScanAndRegister()
    {
        var adapter = BluetoothAdapter.DefaultAdapter;
        if (adapter is null || !adapter.IsEnabled) return;

        var paired = adapter.BondedDevices ?? [];

        foreach (var device in paired)
        {
            // Check if device supports HFP (Handsfree or Headset UUID)
            var uuids = device.GetUuids();
            bool hasHfp = uuids?.Any(u =>
            {
                var uuid = u.Uuid?.ToString();
                return uuid is not null
                    && (uuid.StartsWith("0000111e", StringComparison.OrdinalIgnoreCase)   // HFP
                        || uuid.StartsWith("0000111f", StringComparison.OrdinalIgnoreCase) // HFP AG
                        || uuid.StartsWith("00001108", StringComparison.OrdinalIgnoreCase)); // HSP
            }) ?? false;

            if (!hasHfp) continue;

            var providerId = $"bt:{device.Address}";
            if (_manager.Providers.Any(p => p.ProviderId == providerId))
                continue;

            var provider = new AndroidBluetoothAudioProvider(device, _settings, _context);
            _manager.RegisterProvider(provider);
        }
    }

    /// <summary>
    /// Start listening for BT headset connect/disconnect events.
    /// </summary>
    public void StartListening()
    {
        _receiver = new BluetoothHeadsetReceiver(this);
        var filter = new IntentFilter();
        filter.AddAction(BluetoothHeadset.ActionConnectionStateChanged);
        filter.AddAction(BluetoothDevice.ActionBondStateChanged);
        _context.RegisterReceiver(_receiver, filter);
    }

    /// <summary>
    /// Stop listening for device events.
    /// </summary>
    public void StopListening()
    {
        if (_receiver is not null)
        {
            _context.UnregisterReceiver(_receiver);
            _receiver = null;
        }
    }

    public void Dispose()
    {
        StopListening();
    }

    private sealed class BluetoothHeadsetReceiver : BroadcastReceiver
    {
        private readonly AndroidBluetoothEnumerator _owner;

        public BluetoothHeadsetReceiver(AndroidBluetoothEnumerator owner)
            => _owner = owner;

        public override void OnReceive(Context? context, Intent? intent)
        {
            var action = intent?.Action;

            if (action == BluetoothHeadset.ActionConnectionStateChanged)
            {
                var state = intent!.GetIntExtra(BluetoothProfile.ExtraState, -1);
                var device = intent.GetParcelableExtra(BluetoothDevice.ExtraDevice)
                    as BluetoothDevice;

                if (device is null) return;

                if (state == (int)ProfileState.Connected)
                {
                    _owner.ScanAndRegister();
                }
                else if (state == (int)ProfileState.Disconnected)
                {
                    var providerId = $"bt:{device.Address}";
                    _ = _owner._manager.UnregisterProviderAsync(providerId);
                }
            }
            else if (action == BluetoothDevice.ActionBondStateChanged)
            {
                // New device paired — rescan
                _owner.ScanAndRegister();
            }
        }
    }
}
