using BodyCam.Services.Devices;
using FluentAssertions;

namespace BodyCam.Tests.Services.Devices;

public sealed class DeviceCapabilityRegistryTests
{
    [Fact]
    public void Capabilities_AreIndexedByIdAndKind()
    {
        var camera = new StaticCapability("camera.phone", "phone", DeviceCapabilityKind.CameraCapture, "Phone camera");
        var mic = new StaticCapability("audio.mic", "phone", DeviceCapabilityKind.AudioInput, "Phone mic");
        var registry = new DeviceCapabilityRegistry([mic, camera], []);

        registry.TryGet("camera.phone", out var descriptor).Should().BeTrue();
        descriptor.DisplayName.Should().Be("Phone camera");
        registry.GetByKind(DeviceCapabilityKind.AudioInput).Should().ContainSingle()
            .Which.Id.Should().Be("audio.mic");
    }

    [Fact]
    public void TryGetOperation_ReturnsOperationForCapabilityId()
    {
        var operation = new StaticOperation("camera.phone");
        var registry = new DeviceCapabilityRegistry([], [operation]);

        registry.TryGetOperation("camera.phone", out var found).Should().BeTrue();
        found.Should().BeSameAs(operation);
    }

    private sealed class StaticCapability : IDeviceCapability
    {
        public StaticCapability(
            string id,
            string deviceType,
            DeviceCapabilityKind kind,
            string displayName)
        {
            Descriptor = new DeviceCapabilityDescriptor(id, deviceType, kind, displayName);
        }

        public DeviceCapabilityDescriptor Descriptor { get; }
        public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);
    }

    private sealed class StaticOperation : IDeviceOperation
    {
        public StaticOperation(string capabilityId)
        {
            CapabilityId = capabilityId;
        }

        public string CapabilityId { get; }
        public IReadOnlyList<string> Operations { get; } = ["start"];

        public Task<DeviceOperationResult> ExecuteAsync(
            DeviceOperationRequest request,
            CancellationToken ct = default) =>
            Task.FromResult(new DeviceOperationResult(
                CapabilityId,
                request.Operation,
                Success: true));
    }
}
