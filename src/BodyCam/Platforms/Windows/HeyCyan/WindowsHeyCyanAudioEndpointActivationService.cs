using System.Runtime.InteropServices.WindowsRuntime;
using BodyCam.Platforms.Windows.Audio;
using BodyCam.Services.Glasses.HeyCyan;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using NAudio.CoreAudioApi;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;

namespace BodyCam.Platforms.Windows.HeyCyan;

internal sealed class WindowsHeyCyanAudioEndpointActivationService : IHeyCyanAudioEndpointActivationService
{
    private static readonly Guid HfpServiceUuid = Guid.Parse("0000111e-0000-1000-8000-00805f9b34fb");
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private const int PollAttempts = 15;

    private readonly IHeyCyanGlassesSession _session;
    private readonly WindowsBluetoothEnumerator _inputEnumerator;
    private readonly WindowsBluetoothOutputEnumerator _outputEnumerator;
    private readonly HeyCyanAudioRouter _router;
    private readonly ILogger<WindowsHeyCyanAudioEndpointActivationService> _log;
    private HeyCyanDeviceInfo? _lastTarget;

    public WindowsHeyCyanAudioEndpointActivationService(
        IHeyCyanGlassesSession session,
        WindowsBluetoothEnumerator inputEnumerator,
        WindowsBluetoothOutputEnumerator outputEnumerator,
        HeyCyanAudioRouter router,
        ILogger<WindowsHeyCyanAudioEndpointActivationService> log)
    {
        _session = session;
        _inputEnumerator = inputEnumerator;
        _outputEnumerator = outputEnumerator;
        _router = router;
        _log = log;
    }

    public bool IsSupported => true;

    public bool RequiresActivationBeforeBleConnect => true;

    public HeyCyanAudioEndpointSnapshot? Current { get; private set; }

    public event EventHandler<HeyCyanAudioEndpointSnapshot>? Updated;

    public Task<HeyCyanAudioEndpointSnapshot> RefreshAsync(CancellationToken ct)
        => RefreshCoreAsync(_session.Device ?? _lastTarget, ct);

    public async Task<HeyCyanAudioEndpointSnapshot> BeginActivationAsync(
        HeyCyanDeviceInfo? selectedDevice,
        CancellationToken ct)
    {
        _lastTarget = selectedDevice ?? _session.Device ?? _lastTarget;

        var snapshot = await RefreshCoreAsync(_lastTarget, ct).ConfigureAwait(false);
        if (snapshot.IsReady)
            return snapshot;

        await TrySoftProfileProbeAsync(_lastTarget, ct).ConfigureAwait(false);

        snapshot = await RefreshCoreAsync(_lastTarget, ct).ConfigureAwait(false);
        if (snapshot.IsReady)
            return snapshot;

        await OpenBluetoothSettingsAsync(ct).ConfigureAwait(false);

        for (var i = 0; i < PollAttempts; i++)
        {
            await Task.Delay(PollInterval, ct).ConfigureAwait(false);

            snapshot = await RefreshCoreAsync(_lastTarget, ct).ConfigureAwait(false);
            if (snapshot.IsReady)
                return snapshot;
        }

        return snapshot;
    }

    public async Task OpenBluetoothSettingsAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await MainThread.InvokeOnMainThreadAsync(
            () => Launcher.OpenAsync(new Uri("ms-settings:bluetooth")));
    }

    private async Task<HeyCyanAudioEndpointSnapshot> RefreshCoreAsync(
        HeyCyanDeviceInfo? target,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            await WindowsBluetoothEnumerator.RefreshPairedDeviceCacheAsync().ConfigureAwait(false);
            _inputEnumerator.ScanAndRegister();
            _outputEnumerator.ScanAndRegister();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "HeyCyan audio endpoint rescan failed");
        }

        var targetMac = NormalizeMac(target?.Address);
        var captureEndpoints = EnumerateMatchingEndpoints(DataFlow.Capture, targetMac, target?.Name);
        var renderEndpoints = EnumerateMatchingEndpoints(DataFlow.Render, targetMac, target?.Name);
        var profileNodes = await EnumerateProfileNodesAsync(target, targetMac, ct).ConfigureAwait(false);

        var captureStatus = GetEndpointStatus(captureEndpoints);
        var renderStatus = GetEndpointStatus(renderEndpoints);
        var summary = BuildSummary(target, captureStatus, renderStatus, profileNodes);
        var requiresUserAction = target is not null
            && (captureStatus != HeyCyanEndpointStatus.Active
                || renderStatus != HeyCyanEndpointStatus.Active);

        var snapshot = new HeyCyanAudioEndpointSnapshot(
            target?.Address,
            summary,
            captureStatus,
            renderStatus,
            captureEndpoints,
            renderEndpoints,
            profileNodes,
            requiresUserAction);

        Publish(snapshot);

        if (target is not null && snapshot.HasAnyActiveEndpoint)
            _router.OnBtEndpointRegistered(target.Address);

        return snapshot;
    }

    private static IReadOnlyList<HeyCyanWindowsEndpointInfo> EnumerateMatchingEndpoints(
        DataFlow flow,
        string? targetMac,
        string? targetName)
    {
        using var enumerator = new MMDeviceEnumerator();
        var devices = enumerator.EnumerateAudioEndPoints(flow, DeviceState.All);
        var result = new List<HeyCyanWindowsEndpointInfo>();

        foreach (var device in devices)
        {
            var friendlyName = SafeRead(() => device.FriendlyName) ?? string.Empty;
            var endpointMac = WindowsBluetoothEnumerator.ExtractMacFromDevice(device)
                ?? WindowsBluetoothEnumerator.TryGetMacFromPairedDeviceCache(friendlyName);

            if (!EndpointMatches(friendlyName, endpointMac, targetMac, targetName))
                continue;

            result.Add(new HeyCyanWindowsEndpointInfo(
                friendlyName,
                SafeRead(() => device.ID) ?? string.Empty,
                SafeRead(() => device.State.ToString()) ?? "Unknown",
                endpointMac is null ? null : $"bt:{endpointMac}",
                endpointMac));
        }

        return result;
    }

    private static bool EndpointMatches(
        string friendlyName,
        string? endpointMac,
        string? targetMac,
        string? targetName)
    {
        if (targetMac is not null && NormalizeMac(endpointMac) == targetMac)
            return true;

        if (!string.IsNullOrWhiteSpace(targetName)
            && friendlyName.Contains(targetName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return targetMac is null && IsHeyCyanName(friendlyName);
    }

    private static async Task<IReadOnlyList<HeyCyanWindowsProfileInfo>> EnumerateProfileNodesAsync(
        HeyCyanDeviceInfo? target,
        string? targetMac,
        CancellationToken ct)
    {
        if (target is null)
            return [];

        var result = new List<HeyCyanWindowsProfileInfo>();
        var requestedProperties = new[]
        {
            "System.Devices.DeviceInstanceId",
            "System.Devices.PnpClass"
        };

        try
        {
            var devices = await DeviceInformation
                .FindAllAsync(string.Empty, requestedProperties, DeviceInformationKind.Device)
                .AsTask(ct)
                .ConfigureAwait(false);

            foreach (var device in devices)
            {
                var instanceId = GetProperty(device, "System.Devices.DeviceInstanceId")
                    ?? device.Id;
                var pnpClass = GetProperty(device, "System.Devices.PnpClass")
                    ?? string.Empty;

                if (!ProfileMatches(device.Name, instanceId, target.Name, targetMac))
                    continue;

                result.Add(new HeyCyanWindowsProfileInfo(
                    device.Name,
                    device.IsEnabled ? "Enabled" : "Disabled",
                    pnpClass,
                    instanceId));
            }
        }
        catch
        {
            return [];
        }

        return result;
    }

    private static bool ProfileMatches(
        string name,
        string instanceId,
        string targetName,
        string? targetMac)
    {
        if (!string.IsNullOrWhiteSpace(targetName)
            && name.Contains(targetName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (targetMac is null)
            return IsHeyCyanName(name);

        return NormalizeMac(instanceId) is { } normalizedId
            && normalizedId.Contains(targetMac, StringComparison.OrdinalIgnoreCase);
    }

    private async Task TrySoftProfileProbeAsync(HeyCyanDeviceInfo? target, CancellationToken ct)
    {
        if (target is null)
            return;

        try
        {
            using var classicDevice = await BluetoothDevice
                .FromBluetoothAddressAsync(ParseBluetoothAddress(target.Address))
                .AsTask(ct)
                .ConfigureAwait(false);

            if (classicDevice is null)
                return;

            var hfpId = RfcommServiceId.FromUuid(HfpServiceUuid);
            var services = await classicDevice.GetRfcommServicesForIdAsync(hfpId)
                .AsTask(ct)
                .ConfigureAwait(false);

            if (services.Services.Count > 0)
            {
                _log.LogInformation(
                    "HFP service found via SDP for {Name} ({Mac}); waiting for Windows endpoint activation",
                    target.Name,
                    target.Address);
            }
            else
            {
                _log.LogDebug(
                    "No HFP service found via SDP for {Name} ({Mac})",
                    target.Name,
                    target.Address);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogWarning(
                ex,
                "Soft HFP profile probe failed for {Name} ({Mac})",
                target.Name,
                target.Address);
        }
    }

    private void Publish(HeyCyanAudioEndpointSnapshot snapshot)
    {
        Current = snapshot;
        Updated?.Invoke(this, snapshot);
    }

    private static HeyCyanEndpointStatus GetEndpointStatus(
        IReadOnlyList<HeyCyanWindowsEndpointInfo> endpoints)
    {
        if (endpoints.Count == 0)
            return HeyCyanEndpointStatus.Missing;

        if (endpoints.Any(e => string.Equals(e.State, "Active", StringComparison.OrdinalIgnoreCase)))
            return HeyCyanEndpointStatus.Active;
        if (endpoints.Any(e => string.Equals(e.State, "Disabled", StringComparison.OrdinalIgnoreCase)))
            return HeyCyanEndpointStatus.Disabled;
        if (endpoints.Any(e => string.Equals(e.State, "NotPresent", StringComparison.OrdinalIgnoreCase)))
            return HeyCyanEndpointStatus.NotPresent;
        if (endpoints.Any(e => string.Equals(e.State, "Unplugged", StringComparison.OrdinalIgnoreCase)))
            return HeyCyanEndpointStatus.Unplugged;

        return HeyCyanEndpointStatus.Unknown;
    }

    private static string BuildSummary(
        HeyCyanDeviceInfo? target,
        HeyCyanEndpointStatus captureStatus,
        HeyCyanEndpointStatus renderStatus,
        IReadOnlyList<HeyCyanWindowsProfileInfo> profileNodes)
    {
        if (target is null)
            return "No HeyCyan glasses selected.";

        if (captureStatus == HeyCyanEndpointStatus.Active
            && renderStatus == HeyCyanEndpointStatus.Active)
        {
            return "HeyCyan microphone and speaker ready.";
        }

        if (captureStatus == HeyCyanEndpointStatus.Active)
            return $"HeyCyan microphone ready; speaker is {Describe(renderStatus)}.";

        if (renderStatus == HeyCyanEndpointStatus.Active)
            return $"HeyCyan speaker ready; microphone is {Describe(captureStatus)}.";

        if (profileNodes.Count > 0)
        {
            return $"Windows sees {target.Name}, but audio endpoints are not active "
                + $"(microphone {Describe(captureStatus)}, speaker {Describe(renderStatus)}). "
                + $"Open Bluetooth settings and click Connect for {target.Name}.";
        }

        return $"Windows has not exposed HeyCyan microphone/speaker endpoints for {target.Name}. "
            + "Open Bluetooth settings and connect it as an audio device.";
    }

    private static string Describe(HeyCyanEndpointStatus status) => status switch
    {
        HeyCyanEndpointStatus.Missing => "missing",
        HeyCyanEndpointStatus.NotPresent => "not present",
        HeyCyanEndpointStatus.Unplugged => "unplugged",
        HeyCyanEndpointStatus.Disabled => "disabled",
        HeyCyanEndpointStatus.Active => "ready",
        _ => "unknown"
    };

    private static string? GetProperty(DeviceInformation device, string key)
    {
        return device.Properties.TryGetValue(key, out var value)
            ? value?.ToString()
            : null;
    }

    private static string? SafeRead(Func<string> read)
    {
        try { return read(); }
        catch { return null; }
    }

    private static bool IsHeyCyanName(string value)
        => value.Contains("M01", StringComparison.OrdinalIgnoreCase)
           || value.Contains("HeyCyan", StringComparison.OrdinalIgnoreCase)
           || value.Contains("Cyan", StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeMac(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var chars = value
            .Where(Uri.IsHexDigit)
            .Select(char.ToUpperInvariant)
            .ToArray();

        return chars.Length == 0 ? null : new string(chars);
    }

    private static ulong ParseBluetoothAddress(string address)
    {
        var normalized = NormalizeMac(address);
        if (string.IsNullOrWhiteSpace(normalized))
            throw new FormatException($"Invalid Bluetooth address '{address}'.");

        return Convert.ToUInt64(normalized, 16);
    }
}
