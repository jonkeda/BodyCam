using Microsoft.Extensions.Logging;

namespace BodyCam.Services.Audio;

/// <summary>
/// Generic Bluetooth audio output provider that can enumerate and select
/// from available BT A2DP devices by MAC address.
/// Platform-specific implementations create device-specific providers;
/// this class wraps them and provides MAC-based selection.
/// </summary>
public sealed class BluetoothAudioOutputProvider : IBluetoothAudioOutputProvider
{
    private readonly AudioOutputManager _manager;
    private readonly ILogger<BluetoothAudioOutputProvider> _log;
    private IAudioOutputProvider? _selectedProvider;
    private string? _targetMac;

    public string DisplayName => _selectedProvider?.DisplayName ?? "Bluetooth (no device selected)";
    public string ProviderId => "bluetooth-generic";
    public bool IsAvailable => _selectedProvider?.IsAvailable ?? false;
    public bool IsPlaying => _selectedProvider?.IsPlaying ?? false;
    public int EstimatedOutputLatencyMs => _selectedProvider?.EstimatedOutputLatencyMs ?? 200;
    public AudioOutputCapabilities OutputCapabilities =>
        _selectedProvider?.OutputCapabilities
        ?? AudioCapabilityHeuristics.BluetoothOutput(DisplayName, EstimatedOutputLatencyMs);

    public event EventHandler? Disconnected;
    public event EventHandler? OutputRouteChanged;

    public BluetoothAudioOutputProvider(
        AudioOutputManager manager,
        ILogger<BluetoothAudioOutputProvider> log)
    {
        _manager = manager;
        _log = log;
    }

    /// <summary>
    /// Returns true if a connected BT render endpoint with the specified MAC address exists.
    /// MAC comparison is case-insensitive and tolerates colon vs. dash separators.
    /// </summary>
    public bool HasEndpointWithMac(string? mac)
    {
        if (string.IsNullOrWhiteSpace(mac))
            return false;

        var normalizedTarget = NormalizeMac(mac);
        return _manager.Providers.Any(p =>
        {
            // Check if provider is a BT device provider with matching MAC
            if (!p.ProviderId.StartsWith("bt:", StringComparison.OrdinalIgnoreCase))
                return false;

            var providerMac = p.ProviderId.Substring(3); // strip "bt:" prefix
            return NormalizeMac(providerMac) == normalizedTarget;
        });
    }

    /// <summary>
    /// Locks subsequent StartAsync calls to the endpoint with the specified MAC address.
    /// Must be called before StartAsync.
    /// </summary>
    public Task SelectEndpointByMacAsync(string mac, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(mac))
            throw new ArgumentException("MAC address cannot be null or empty.", nameof(mac));

        var normalizedTarget = NormalizeMac(mac);
        var provider = _manager.Providers.FirstOrDefault(p =>
        {
            if (!p.ProviderId.StartsWith("bt:", StringComparison.OrdinalIgnoreCase))
                return false;

            var providerMac = p.ProviderId.Substring(3);
            return NormalizeMac(providerMac) == normalizedTarget;
        });

        if (provider is null)
            throw new InvalidOperationException(
                $"No Bluetooth audio output provider found for MAC {mac}. " +
                "Ensure the device is paired and supports A2DP.");

        // Unhook old provider if different
        if (_selectedProvider is not null && _selectedProvider != provider)
        {
            _selectedProvider.Disconnected -= OnDisconnected;
        }

        _selectedProvider = provider;
        _targetMac = normalizedTarget;

        // Hook events
        _selectedProvider.Disconnected -= OnDisconnected; // defensive unhook
        _selectedProvider.Disconnected += OnDisconnected;

        _log.LogInformation("Selected BT render endpoint: {DisplayName} ({Mac})",
            _selectedProvider.DisplayName, mac);

        return Task.CompletedTask;
    }

    public async Task StartAsync(int sampleRate, CancellationToken ct = default)
    {
        if (_selectedProvider is null)
            throw new InvalidOperationException(
                "No Bluetooth endpoint selected. Call SelectEndpointByMacAsync first.");

        await _selectedProvider.StartAsync(sampleRate, ct);
    }

    public Task StopAsync()
    {
        if (_selectedProvider is null)
            return Task.CompletedTask;

        return _selectedProvider.StopAsync();
    }

    public Task PlayChunkAsync(byte[] pcmData, CancellationToken ct = default)
    {
        if (_selectedProvider is null)
            throw new InvalidOperationException("No Bluetooth endpoint selected.");

        return _selectedProvider.PlayChunkAsync(pcmData, ct);
    }

    public void ClearBuffer()
    {
        _selectedProvider?.ClearBuffer();
    }

    public async Task FadeOutAndClearAsync(int fadeMs = 30, CancellationToken ct = default)
    {
        if (_selectedProvider is not null)
            await _selectedProvider.FadeOutAndClearAsync(fadeMs, ct);
    }

    private void OnDisconnected(object? sender, EventArgs e)
    {
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask DisposeAsync()
    {
        if (_selectedProvider is not null)
        {
            _selectedProvider.Disconnected -= OnDisconnected;

            if (_selectedProvider is IAsyncDisposable ad)
                await ad.DisposeAsync();
        }
    }

    /// <summary>
    /// Normalize MAC address to lowercase with no separators for comparison.
    /// AA:BB:CC:DD:EE:FF or aa-bb-cc-dd-ee-ff -> aabbccddeeff
    /// </summary>
    private static string NormalizeMac(string mac)
    {
        return mac.Replace(":", "").Replace("-", "").ToLowerInvariant();
    }
}
