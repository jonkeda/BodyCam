using Microsoft.Extensions.Logging;

namespace BodyCam.Services.Audio;

/// <summary>
/// Generic Bluetooth audio input provider that can enumerate and select
/// from available BT HFP/HSP devices by MAC address.
/// Platform-specific implementations create device-specific providers;
/// this class wraps them and provides MAC-based selection.
/// </summary>
public sealed class BluetoothAudioInputProvider : IBluetoothAudioInputProvider
{
    private readonly AudioInputManager _manager;
    private readonly ILogger<BluetoothAudioInputProvider> _log;
    private IAudioInputProvider? _selectedProvider;
    private string? _targetMac;

    public string DisplayName => _selectedProvider?.DisplayName ?? "Bluetooth (no device selected)";
    public string ProviderId => "bluetooth-generic";
    public bool IsAvailable => _selectedProvider?.IsAvailable ?? false;
    public bool IsCapturing => _selectedProvider?.IsCapturing ?? false;

    public event EventHandler<byte[]>? AudioChunkAvailable;
    public event EventHandler? Disconnected;

    public BluetoothAudioInputProvider(
        AudioInputManager manager,
        ILogger<BluetoothAudioInputProvider> log)
    {
        _manager = manager;
        _log = log;
    }

    /// <summary>
    /// Returns true if a connected BT capture endpoint with the specified MAC address exists.
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
                $"No Bluetooth audio provider found for MAC {mac}. " +
                "Ensure the device is paired and supports HFP/HSP.");

        // Unhook old provider if different
        if (_selectedProvider is not null && _selectedProvider != provider)
        {
            _selectedProvider.AudioChunkAvailable -= OnAudioChunk;
            _selectedProvider.Disconnected -= OnDisconnected;
        }

        _selectedProvider = provider;
        _targetMac = normalizedTarget;

        // Hook events
        _selectedProvider.AudioChunkAvailable -= OnAudioChunk; // defensive unhook
        _selectedProvider.AudioChunkAvailable += OnAudioChunk;
        _selectedProvider.Disconnected -= OnDisconnected;
        _selectedProvider.Disconnected += OnDisconnected;

        _log.LogInformation("Selected BT endpoint: {DisplayName} ({Mac})",
            _selectedProvider.DisplayName, mac);

        return Task.CompletedTask;
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_selectedProvider is null)
            throw new InvalidOperationException(
                "No Bluetooth endpoint selected. Call SelectEndpointByMacAsync first.");

        await _selectedProvider.StartAsync(ct);
    }

    public Task StopAsync()
    {
        if (_selectedProvider is null)
            return Task.CompletedTask;

        return _selectedProvider.StopAsync();
    }

    private void OnAudioChunk(object? sender, byte[] chunk)
    {
        AudioChunkAvailable?.Invoke(this, chunk);
    }

    private void OnDisconnected(object? sender, EventArgs e)
    {
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask DisposeAsync()
    {
        if (_selectedProvider is not null)
        {
            _selectedProvider.AudioChunkAvailable -= OnAudioChunk;
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
