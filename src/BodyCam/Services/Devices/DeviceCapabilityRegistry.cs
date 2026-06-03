namespace BodyCam.Services.Devices;

public sealed class DeviceCapabilityRegistry : IDeviceCapabilityRegistry
{
    private readonly IReadOnlyDictionary<string, DeviceCapabilityDescriptor> _capabilities;
    private readonly IReadOnlyDictionary<string, IDeviceOperation> _operations;

    public DeviceCapabilityRegistry(
        IEnumerable<IDeviceCapability> capabilities,
        IEnumerable<IDeviceOperation> operations)
    {
        _capabilities = capabilities.ToDictionary(
            capability => capability.Descriptor.Id,
            capability => capability.Descriptor,
            StringComparer.OrdinalIgnoreCase);
        _operations = operations.ToDictionary(
            operation => operation.CapabilityId,
            StringComparer.OrdinalIgnoreCase);

        Capabilities = _capabilities.Values
            .OrderBy(capability => capability.DeviceType)
            .ThenBy(capability => capability.DisplayName)
            .ToArray();
    }

    public IReadOnlyList<DeviceCapabilityDescriptor> Capabilities { get; }

    public IReadOnlyList<DeviceCapabilityDescriptor> GetByKind(DeviceCapabilityKind kind) =>
        Capabilities
            .Where(capability => capability.Kind == kind)
            .ToArray();

    public bool TryGet(string capabilityId, out DeviceCapabilityDescriptor descriptor) =>
        _capabilities.TryGetValue(capabilityId, out descriptor!);

    public bool TryGetOperation(string capabilityId, out IDeviceOperation operation) =>
        _operations.TryGetValue(capabilityId, out operation!);
}
