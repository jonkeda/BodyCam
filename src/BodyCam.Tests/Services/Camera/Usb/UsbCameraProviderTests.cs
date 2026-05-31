using BodyCam.Services.Camera.Usb;
using BodyCam.Tests.Services.Glasses.HeyCyan.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace BodyCam.Tests.Services.Camera.Usb;

public sealed class UsbCameraProviderTests
{
    [Fact]
    public async Task StartAsync_WhenMatchingDeviceExists_MakesProviderAvailable()
    {
        var client = Substitute.For<IUsbCameraClient>();
        client.IsSupported.Returns(true);
        client.EnumerateAsync(Arg.Any<CancellationToken>())
            .Returns([
                new UsbCameraDeviceInfo(
                    "USB\\VID_349C&PID_0411\\test",
                    "HD camera",
                    true,
                    ["YUY2 640x480 30fps"])
            ]);

        var provider = CreateProvider(new FakeSettingsService(), client);

        await provider.StartAsync();

        provider.ProviderId.Should().Be(UsbCameraProvider.Id);
        provider.DisplayName.Should().Be("USB Camera");
        provider.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task StartAsync_WhenNoDeviceMatches_RemainsUnavailable()
    {
        var client = Substitute.For<IUsbCameraClient>();
        client.IsSupported.Returns(true);
        client.EnumerateAsync(Arg.Any<CancellationToken>())
            .Returns([
                new UsbCameraDeviceInfo("USB\\VID_OTHER", "Other Camera", true, [])
            ]);

        var provider = CreateProvider(new FakeSettingsService(), client);

        await provider.StartAsync();

        provider.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task CaptureFrameAsync_WhenClientReturnsJpeg_ReturnsBytes()
    {
        var jpeg = new byte[] { 0xff, 0xd8, 0xff, 0xe0, 0x00, 0x10, 0xff, 0xd9 };
        var client = Substitute.For<IUsbCameraClient>();
        client.IsSupported.Returns(true);
        client.EnumerateAsync(Arg.Any<CancellationToken>())
            .Returns([
                new UsbCameraDeviceInfo("USB\\VID_349C&PID_0411", "HD camera", true, [])
            ]);

        UsbCameraCaptureOptions? options = null;
        client.CaptureJpegAsync(Arg.Any<UsbCameraCaptureOptions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                options = call.ArgAt<UsbCameraCaptureOptions>(0);
                return new UsbCameraCaptureResult(true, jpeg, "USB\\VID_349C&PID_0411", "HD camera", null);
            });

        var provider = CreateProvider(new FakeSettingsService(), client);

        var frame = await provider.CaptureFrameAsync();

        frame.Should().Equal(jpeg);
        provider.IsAvailable.Should().BeTrue();
        options.Should().NotBeNull();
        options!.DeviceMatch.Should().Be(UsbCameraProvider.DefaultDeviceMatch);
    }

    [Fact]
    public async Task CaptureFrameAsync_WhenClientFails_ReturnsNull()
    {
        var client = Substitute.For<IUsbCameraClient>();
        client.IsSupported.Returns(true);
        client.EnumerateAsync(Arg.Any<CancellationToken>())
            .Returns([
                new UsbCameraDeviceInfo("USB\\VID_349C&PID_0411", "HD camera", true, [])
            ]);
        client.CaptureJpegAsync(Arg.Any<UsbCameraCaptureOptions>(), Arg.Any<CancellationToken>())
            .Returns(UsbCameraCaptureResult.Failed("camera unavailable"));

        var provider = CreateProvider(new FakeSettingsService(), client);

        var frame = await provider.CaptureFrameAsync();

        frame.Should().BeNull();
        provider.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task CaptureFrameAsync_UsesSavedDeviceMatch()
    {
        var client = Substitute.For<IUsbCameraClient>();
        client.IsSupported.Returns(true);
        client.EnumerateAsync(Arg.Any<CancellationToken>())
            .Returns([
                new UsbCameraDeviceInfo("USB\\VID_1234&PID_5678", "Lab Camera", true, [])
            ]);
        client.CaptureJpegAsync(Arg.Any<UsbCameraCaptureOptions>(), Arg.Any<CancellationToken>())
            .Returns(new UsbCameraCaptureResult(
                true,
                [0xff, 0xd8, 0xff, 0xd9],
                "USB\\VID_1234&PID_5678",
                "Lab Camera",
                null));

        var provider = CreateProvider(
            new FakeSettingsService { UsbCameraDeviceMatch = " Lab Camera " },
            client);

        await provider.CaptureFrameAsync();

        await client.Received(1)
            .CaptureJpegAsync(
                Arg.Is<UsbCameraCaptureOptions>(options => options.DeviceMatch == "Lab Camera"),
                Arg.Any<CancellationToken>());
    }

    private static UsbCameraProvider CreateProvider(
        FakeSettingsService settings,
        IUsbCameraClient client)
    {
        return new UsbCameraProvider(
            settings,
            client,
            NullLogger<UsbCameraProvider>.Instance);
    }
}

