using BodyCam.Services.Audio;

namespace BodyCam.Tests.Services.Glasses.HeyCyan.Fakes;

/// <summary>
/// Fake Bluetooth audio output provider for unit testing HeyCyanAudioOutputProvider.
/// Tracks MAC selection, start/stop calls, and captures played audio chunks for verification.
/// </summary>
public sealed class FakeBluetoothAudioOutputProvider : IBluetoothAudioOutputProvider
{
    private readonly HashSet<string> _macs;
    private readonly List<byte[]> _playedChunks = new();

    public FakeBluetoothAudioOutputProvider(IEnumerable<string> macsAvailable)
        => _macs = new(macsAvailable, StringComparer.OrdinalIgnoreCase);

    public string ProviderId  => "bluetooth";
    public string DisplayName => "BT Speaker (fake)";
    public bool IsAvailable  => true;
    public bool IsPlaying { get; private set; }
    public int EstimatedOutputLatencyMs => 200;

    public string? SelectedMac { get; private set; }
    public int StartCount { get; private set; }
    public int StopCount  { get; private set; }
    public IReadOnlyList<byte[]> PlayedChunks => _playedChunks.AsReadOnly();

    public event EventHandler? Disconnected;
    public event EventHandler? OutputRouteChanged;

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

    public Task StartAsync(int sampleRate, CancellationToken ct = default)
    {
        StartCount++;
        IsPlaying = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        StopCount++;
        IsPlaying = false;
        return Task.CompletedTask;
    }

    public Task PlayChunkAsync(byte[] pcmData, CancellationToken ct = default)
    {
        _playedChunks.Add(pcmData);
        return Task.CompletedTask;
    }

    public void ClearBuffer()
    {
        _playedChunks.Clear();
    }

    public Task FadeOutAndClearAsync(int fadeMs = 30, CancellationToken ct = default)
    {
        ClearBuffer();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Test helper: simulate BT disconnection.
    /// </summary>
    public void RaiseDisconnected()
        => Disconnected?.Invoke(this, EventArgs.Empty);

    public ValueTask DisposeAsync() => default;
}
