namespace BodyCam.Services.Devices;

public enum DeviceCapabilityKind
{
    CameraCapture,
    AudioInput,
    AudioOutput,
    ButtonInput,
    WakeWord,
    MediaTransfer,
    Reconnect,
    Diagnostics,
    Battery,
    Settings,
}

public sealed record DeviceCapabilityDescriptor(
    string Id,
    string DeviceType,
    DeviceCapabilityKind Kind,
    string DisplayName,
    IReadOnlyDictionary<string, string>? Properties = null);

public sealed record DeviceOperationRequest(
    string CapabilityId,
    string Operation,
    IReadOnlyDictionary<string, object?>? Parameters = null);

public sealed record DeviceOperationResult(
    string CapabilityId,
    string Operation,
    bool Success,
    object? Data = null,
    string? Error = null);

public interface IDeviceCapability
{
    DeviceCapabilityDescriptor Descriptor { get; }
    Task<bool> IsAvailableAsync(CancellationToken ct = default);
}

public interface IDeviceOperation
{
    string CapabilityId { get; }
    IReadOnlyList<string> Operations { get; }
    Task<DeviceOperationResult> ExecuteAsync(
        DeviceOperationRequest request,
        CancellationToken ct = default);
}

public interface IDeviceCapabilityRegistry
{
    IReadOnlyList<DeviceCapabilityDescriptor> Capabilities { get; }
    IReadOnlyList<DeviceCapabilityDescriptor> GetByKind(DeviceCapabilityKind kind);
    bool TryGet(string capabilityId, out DeviceCapabilityDescriptor descriptor);
    bool TryGetOperation(string capabilityId, out IDeviceOperation operation);
}
