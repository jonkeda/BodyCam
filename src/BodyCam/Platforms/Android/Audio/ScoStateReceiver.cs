using Android.Content;
using Android.Media;

namespace BodyCam.Platforms.Android.Audio;

/// <summary>
/// BroadcastReceiver that completes when BT SCO audio is connected.
/// </summary>
internal sealed class ScoStateReceiver : BroadcastReceiver
{
    private readonly TaskCompletionSource _tcs;

    public ScoStateReceiver(TaskCompletionSource tcs) => _tcs = tcs;

    public override void OnReceive(Context? context, Intent? intent)
    {
        var state = intent?.GetIntExtra(AudioManager.ExtraScoAudioState, -1);
        if (state == (int)ScoAudioState.Connected)
            _tcs.TrySetResult();
        else if (state == (int)ScoAudioState.Error)
            _tcs.TrySetException(new InvalidOperationException("BT SCO connection failed."));
    }
}
