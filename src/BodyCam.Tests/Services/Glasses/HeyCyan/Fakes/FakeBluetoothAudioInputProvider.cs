using BodyCam.Services.Audio;

namespace BodyCam.Tests.Services.Glasses.HeyCyan.Fakes;

/// <summary>
/// Fake Bluetooth audio input provider for unit testing HeyCyanAudioInputProvider.
/// Tracks MAC selection, start/stop calls, and allows tests to emit chunks and disconnect events.
/// </summary>
public sealed class FakeBluetoothAudioInputProvider : IBluetoothAudioInputProvider
{
    private readonly HashSet<string> _macs;

    public FakeBluetoothAudioInputProvider(IEnumerable<string> macsAvailable)
        => _macs = new(macsAvailable, StringComparer.OrdinalIgnoreCase);

    public string ProviderId  => "bluetooth";
    public string DisplayName => "BT Mic (fake)";
    public bool IsAvailable  => true;
    public bool IsCapturing  { get; private set; }

    public string? SelectedMac { get; private set; }
    public int StartCount { get; private set; }
    public int StopCount  { get; private set; }

    public event EventHandler<byte[]>? AudioChunkAvailable;
    public event EventHandler? Disconnected;

    public bool HasEndpointWithMac(string? mac)
    {
        if (mac is null) return false;
        // Normalize MAC: replace dashes with colons for comparison
        var normalized = mac.Replace('-', ':');
        return _macs.Contains(normalized);
    }

    public Task SelectEndpointByMacAsync(string mac, CancellationToken ct)
    {
        SelectedMac = mac;
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        StartCount++;
        IsCapturing = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        StopCount++;
        IsCapturing = false;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Test helper: simulate receiving a PCM audio chunk from the BT mic.
    /// </summary>
    public void RaiseChunk(byte[] pcm)
        => AudioChunkAvailable?.Invoke(this, pcm);

    /// <summary>
    /// Test helper: simulate BT disconnection.
    /// </summary>
    public void RaiseDisconnected()
        => Disconnected?.Invoke(this, EventArgs.Empty);

    public ValueTask DisposeAsync() => default;
}
