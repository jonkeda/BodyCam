using Android.Bluetooth;
using Android.Content;
using Android.Media;
using BodyCam.Services.Audio;

namespace BodyCam.Platforms.Android.Audio;

/// <summary>
/// Discovers Bluetooth audio output devices on Android and registers them
/// with <see cref="AudioOutputManager"/>. Listens for BT connection state changes
/// via <see cref="BroadcastReceiver"/>.
/// </summary>
public sealed class AndroidBluetoothOutputEnumerator : IDisposable
{
    private readonly AudioOutputManager _manager;
    private readonly Context _context;
    private readonly AudioManager? _audioManager;
    private BluetoothOutputReceiver? _receiver;

    public AndroidBluetoothOutputEnumerator(AudioOutputManager manager)
    {
        _manager = manager;
        _context = global::Android.App.Application.Context;
        _audioManager = (AudioManager?)_context.GetSystemService(Context.AudioService);
    }

    /// <summary>
    /// Scan for BT audio output devices and register providers.
    /// </summary>
    public void ScanAndRegister()
    {
        if (_audioManager is null) return;

        var devices = _audioManager.GetDevices(GetDevicesTargets.Outputs);
        if (devices is null) return;

        foreach (var device in devices)
        {
            if (device.Type is not (AudioDeviceType.BluetoothA2dp
                                or AudioDeviceType.BluetoothSco))
                continue;

            var providerId = $"bt-out:{device.Id}";
            if (_manager.Providers.Any(p => p.ProviderId == providerId))
                continue;

            var provider = new AndroidBluetoothAudioOutputProvider(device, _context);
            _manager.RegisterProvider(provider);
        }
    }

    /// <summary>
    /// Start listening for BT device connection/disconnection.
    /// </summary>
    public void StartListening()
    {
        _receiver = new BluetoothOutputReceiver(this);
        var filter = new IntentFilter();
        filter.AddAction(BluetoothDevice.ActionAclConnected);
        filter.AddAction(BluetoothDevice.ActionAclDisconnected);
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

    private sealed class BluetoothOutputReceiver : BroadcastReceiver
    {
        private readonly AndroidBluetoothOutputEnumerator _owner;

        public BluetoothOutputReceiver(AndroidBluetoothOutputEnumerator owner)
            => _owner = owner;

        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent?.Action is null) return;

            switch (intent.Action)
            {
                case BluetoothDevice.ActionAclConnected:
                    _owner.ScanAndRegister();
                    break;

                case BluetoothDevice.ActionAclDisconnected:
                    // Provider's AudioDeviceCallback fires Disconnected for fallback.
                    // Rescan to clean up stale providers.
                    _owner.ScanAndRegister();
                    break;
            }
        }
    }
}
